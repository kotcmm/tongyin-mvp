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

func backfillLegacyUsers(db Repository) error{
 owners:=map[string]bool{}
 for _,w:=range db.works("",true){if w.Owner!=""{owners[w.Owner]=true}}
 for _,o:=range db.orders("",true){if o.Owner!=""{owners[o.Owner]=true}}
 for owner:=range owners{if _,ok:=db.user(owner);ok{delete(owners,owner)}}
 if len(owners)==0{return nil}
 return db.update(func(d *Data)error{
  if d.Users==nil{d.Users=map[string]*User{}}
  for owner:=range owners{
   if _,ok:=d.Users[owner];!ok{d.Users[owner]=&User{ID:newID(),Owner:owner,CreatedAt:now()}}
  }
  return nil
 })
}

func (a *App) adminDashboard(w http.ResponseWriter,r *http.Request){
 if !a.admin(w,r){return}
 works:=a.db.works("",true);orders:=a.db.orders("",true)
 metrics:=map[string]int{"users":0,"works":len(works),"orders":len(orders),"image_pending":0,"model_pending":0,"awaiting_payment":0,"paid":0,"printing":0,"shipped":0,"completed":0,"refund_requested":0}
 for _,v:=range works{if _,ok:=metrics[v.Status];ok{metrics[v.Status]++}}
 revenue:=0
 for _,v:=range orders{
  if _,ok:=metrics[v.Status];ok{metrics[v.Status]++}
  if v.RefundStatus=="requested"{metrics["refund_requested"]++}
  if v.Status!="awaiting_payment"&&v.Status!="canceled"{revenue+=v.PriceFen}
 }
 _=a.db.update(func(d *Data)error{metrics["users"]=len(d.Users);return nil})
 reply(w,200,map[string]any{"metrics":metrics,"paid_gross_fen":revenue,"pay_configured":a.payReady(),"tracking_configured":a.cfg.KuaidiKey!=""&&a.cfg.KuaidiCustomer!=""})
}
func (a *App) adminUsers(w http.ResponseWriter,r *http.Request){
 if !a.admin(w,r){return}
 var list []User
 err:=a.db.update(func(d *Data)error{for _,u:=range d.Users{list=append(list,*u)};return nil})
 if err!=nil{fail(w,500,"用户列表加载失败");return}
 keyword:=strings.ToLower(strings.TrimSpace(r.URL.Query().Get("q")))
 works:=a.db.works("",true);orders:=a.db.orders("",true)
 result:=[]map[string]any{}
 for _,u:=range list{
  if keyword!="" && !strings.Contains(strings.ToLower(u.ID),keyword){continue}
  wc,oc:=0,0
  for _,v:=range works{if v.Owner==u.Owner{wc++}}
  for _,v:=range orders{if v.Owner==u.Owner{oc++}}
  result=append(result,map[string]any{"id":u.ID,"created_at":u.CreatedAt,"last_login":u.LastLogin,"disabled":u.Disabled,"works":wc,"orders":oc})
 }
 sort.Slice(result,func(i,j int)bool{return result[i]["created_at"].(string)>result[j]["created_at"].(string)})
 reply(w,200,result)
}
func (a *App) adminUserStatus(w http.ResponseWriter,r *http.Request){
 if !a.admin(w,r){return}
 var q struct{Disabled bool `json:"disabled"`}
 if decode(r,&q)!=nil{fail(w,400,"参数错误");return}
 id:=r.PathValue("id")
 err:=a.db.update(func(d *Data)error{
  for _,u:=range d.Users{
   if u.ID==id{
    u.Disabled=q.Disabled
    actor,_,_:=a.adminIdentity(r)
    recordAudit(d,actor,"user.status",id,fmt.Sprintf("disabled=%v",q.Disabled))
    return nil
   }
  }
  return errNotFound
 })
 if errors.Is(err,errNotFound){fail(w,404,"用户不存在");return}
 if err!=nil{fail(w,500,"修改失败");return}
 reply(w,200,map[string]string{"status":"ok"})
}
func (a *App) adminAudit(w http.ResponseWriter,r *http.Request){
 if !a.admin(w,r){return}
 items:=[]*AuditRecord{}
 err:=a.db.update(func(d *Data)error{for _,v:=range d.Audit{x:=*v;items=append(items,&x)};return nil})
 if err!=nil{fail(w,500,"加载失败");return}
 sort.Slice(items,func(i,j int)bool{return items[i].CreatedAt>items[j].CreatedAt})
 if len(items)>300{items=items[:300]}
 reply(w,200,items)
}

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
func (a *App) adminRefundRequest(w http.ResponseWriter,r *http.Request){
 if !a.admin(w,r){return}
 // No fake "refunded" transition: operator must complete a real merchant refund.
 var q struct{Reason string `json:"reason"`}
 if decode(r,&q)!=nil{fail(w,400,"参数错误");return}
 q.Reason=strings.TrimSpace(q.Reason)
 if len([]rune(q.Reason))<3||len([]rune(q.Reason))>200{fail(w,400,"请填写退款原因");return}
 actor,_,_:=a.adminIdentity(r)
 err:=a.db.update(func(d *Data)error{
  o,ok:=d.Orders[r.PathValue("id")];if !ok{return errNotFound}
  if o.Status!="paid"&&o.Status!="printing"{return errors.New("只有已付款或制作中的订单能申请退款")}
  if o.RefundStatus!=""{return errors.New("该订单已经有退款记录")}
  o.RefundStatus="requested"
  o.History=append(o.History,OrderHistory{At:now(),Actor:actor,Action:"refund_requested",Note:q.Reason})
  recordAudit(d,actor,"order.refund_requested",o.ID,q.Reason)
  return nil
 })
 if errors.Is(err,errNotFound){fail(w,404,"订单不存在");return}
 if err!=nil{fail(w,409,err.Error());return}
 reply(w,200,map[string]any{"refund_status":"requested","message":"已记录退款申请，请在微信商户平台执行实际退款并核对资金；系统未宣称退款成功"})
}
