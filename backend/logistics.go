package main

import (
	"crypto/md5"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"regexp"
	"sort"
	"strings"
	"time"
)

// 物流模块：真实运单与快递平台轨迹；没有真实记录时不生成虚构节点。
func (a *App) orderTracking(w http.ResponseWriter,r *http.Request){
 owner:=a.uid(w,r);if owner==""{return}
 order,ok:=a.db.order(r.PathValue("id"))
 if !ok||order.Owner!=owner{fail(w,404,"订单不存在");return}
 if order.Status!="shipped"&&order.Status!="completed"{fail(w,409,"订单尚未发货");return}
 reply(w,200,map[string]any{
  "company":order.Carrier,"company_code":order.CarrierCode,
  "tracking_no":order.Tracking,"tracking_state":order.TrackingState,
  "updated_at":order.TrackingCheckedAt,"events":emptyTracking(order.TrackingEvents),
  "source":trackingSource(order.TrackingEvents),
 })
}

func emptyTracking(events []TrackingEvent) []TrackingEvent {
 if len(events)==0{return []TrackingEvent{}}
 return events
}

func trackingSource(events []TrackingEvent) string{
 if len(events)==0{return "unavailable"}
 for _,e:=range events{if e.Source=="kuaidi100"{return "kuaidi100"}}
 return "manual"
}
var shipmentCode=regexp.MustCompile(`^[a-z0-9]{2,30}$`)
var trackingNumber=regexp.MustCompile(`^[A-Za-z0-9_-]{5,40}$`)

func (a *App) adminShipment(w http.ResponseWriter,r *http.Request){
 if !a.admin(w,r){return}
 var q struct{
  Carrier string `json:"carrier"`
  CarrierCode string `json:"carrier_code"`
  Tracking string `json:"tracking"`
 }
 if decode(r,&q)!=nil{fail(w,400,"参数错误");return}
 q.Carrier=strings.TrimSpace(q.Carrier)
 q.CarrierCode=strings.ToLower(strings.TrimSpace(q.CarrierCode))
 q.Tracking=strings.TrimSpace(q.Tracking)
 if len([]rune(q.Carrier))<2||len([]rune(q.Carrier))>40 || !shipmentCode.MatchString(q.CarrierCode)||!trackingNumber.MatchString(q.Tracking){
  fail(w,400,"请填写快递公司、快递100编码和有效运单号");return
 }
 actor,_,_:=a.adminIdentity(r)
 err:=a.db.update(func(d *Data)error{
  o,ok:=d.Orders[r.PathValue("id")];if !ok{return errNotFound}
  if o.Status!="printing"{return errors.New("必须在制作中状态完成打包后才能发货")}
  o.Carrier=q.Carrier;o.CarrierCode=q.CarrierCode;o.Tracking=q.Tracking
  o.Status="shipped";o.ShippedAt=now();o.TrackingState="unavailable"
  o.TrackingEvents=[]TrackingEvent{}
  o.TrackingCheckedAt=""
  o.History=append(o.History,OrderHistory{At:now(),Actor:actor,Action:"shipped",Note:q.Carrier+" "+q.Tracking})
  recordAudit(d,actor,"order.shipped",o.ID,q.Carrier)
  return nil
 })
 if errors.Is(err,errNotFound){fail(w,404,"订单不存在");return}
 if err!=nil{fail(w,409,err.Error());return}
 reply(w,200,map[string]string{"status":"shipped"})
}

func (a *App) adminTrackingEntry(w http.ResponseWriter,r *http.Request){
 if !a.admin(w,r){return}
 var q struct{Time string `json:"time"`;Context string `json:"context"`}
 if decode(r,&q)!=nil{fail(w,400,"参数错误");return}
 q.Time=strings.TrimSpace(q.Time);q.Context=strings.TrimSpace(q.Context)
 if len([]rune(q.Context))<3||len([]rune(q.Context))>300||len(q.Time)<10||len(q.Time)>40{
  fail(w,400,"请填写物流时间和真实物流内容");return
 }
 actor,_,_:=a.adminIdentity(r)
 err:=a.db.update(func(d *Data)error{
  o,ok:=d.Orders[r.PathValue("id")];if !ok{return errNotFound}
  if o.Status!="shipped"&&o.Status!="completed"{return errors.New("订单尚未发货")}
  for _,e:=range o.TrackingEvents{if e.Time==q.Time&&e.Context==q.Context{return nil}}
  o.TrackingEvents=append(o.TrackingEvents,TrackingEvent{Time:q.Time,Context:q.Context,Source:"manual"})
  sort.Slice(o.TrackingEvents,func(i,j int)bool{return o.TrackingEvents[i].Time>o.TrackingEvents[j].Time})
  o.TrackingCheckedAt=now();o.TrackingState="manual"
  recordAudit(d,actor,"tracking.manual",o.ID,q.Context)
  return nil
 })
 if errors.Is(err,errNotFound){fail(w,404,"订单不存在");return}
 if err!=nil{fail(w,409,err.Error());return}
 reply(w,200,map[string]string{"status":"ok"})
}

func (a *App) adminTrackingRefresh(w http.ResponseWriter,r *http.Request){
 if !a.admin(w,r){return}
 if a.cfg.KuaidiCustomer==""||a.cfg.KuaidiKey==""{fail(w,503,"未配置快递100查询接口凭证");return}
 order,ok:=a.db.order(r.PathValue("id"))
 if !ok{fail(w,404,"订单不存在");return}
 if order.Status!="shipped"&&order.Status!="completed"{fail(w,409,"订单尚未发货");return}
 if order.CarrierCode==""||order.Tracking==""{fail(w,409,"请先录入快递公司和运单号");return}
 if last,err:=time.Parse(time.RFC3339,order.TrackingCheckedAt);err==nil&&time.Since(last)<30*time.Minute{
  fail(w,429,"物流接口至少间隔30分钟查询一次");return
 }
 // Kuaidi100 real-time query: MD5(param + key + customer), uppercase.
 paramBytes,_:=json.Marshal(map[string]string{"com":order.CarrierCode,"num":order.Tracking,"phone":order.Phone,"order":"desc"})
 param:=string(paramBytes)
 checksum:=md5.Sum([]byte(param+a.cfg.KuaidiKey+a.cfg.KuaidiCustomer))
 form:=url.Values{"customer":{a.cfg.KuaidiCustomer},"sign":{strings.ToUpper(hex.EncodeToString(checksum[:]))},"param":{param}}
 client:=a.client
 if client==nil{client=&http.Client{Timeout:15*time.Second}}
 req,err:=http.NewRequestWithContext(r.Context(),"POST","https://poll.kuaidi100.com/poll/query.do",strings.NewReader(form.Encode()))
 if err!=nil{fail(w,500,"查询请求错误");return}
 req.Header.Set("Content-Type","application/x-www-form-urlencoded")
 resp,err:=client.Do(req)
 if err!=nil{fail(w,502,"物流平台暂不可访问");return}
 defer resp.Body.Close()
 body,err:=io.ReadAll(io.LimitReader(resp.Body,256<<10))
 if err!=nil{fail(w,502,"物流返回异常");return}
 var result struct{
  Status string `json:"status"`
  State string `json:"state"`
  Message string `json:"message"`
  IsCheck string `json:"ischeck"`
  Data []struct{Time string `json:"time"`;Context string `json:"context"`} `json:"data"`
 }
 if json.Unmarshal(body,&result)!=nil||resp.StatusCode!=200||result.Status!="200"{
  fail(w,502,"物流平台暂无法查询："+result.Message);return
 }
 events:=[]TrackingEvent{}
 for _,v:=range result.Data{
  if v.Time!=""&&v.Context!=""{events=append(events,TrackingEvent{Time:v.Time,Context:v.Context,Source:"kuaidi100"})}
 }
 if len(events)>100{events=events[:100]}
 sort.Slice(events,func(i,j int)bool{return events[i].Time>events[j].Time})
 actor,_,_:=a.adminIdentity(r)
 err=a.db.update(func(d *Data)error{
  o,ok:=d.Orders[order.ID]
  if !ok{return errNotFound}
  if o.Tracking!=order.Tracking||o.CarrierCode!=order.CarrierCode{return errors.New("运单信息已变化，请重新查询")}
  o.TrackingEvents=events
  o.TrackingCheckedAt=now()
  o.TrackingState=result.State
  if result.State=="3"||result.IsCheck=="1"{o.TrackingState="delivered";o.DeliveredAt=now()}
  recordAudit(d,actor,"tracking.refreshed",o.ID,fmt.Sprintf("%d events",len(events)))
  return nil
 })
 if err!=nil{fail(w,409,"物流保存失败");return}
 reply(w,200,map[string]any{"events":events,"tracking_state":result.State})
}
