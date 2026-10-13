package main

import (
 "net/http"
 "net/http/httptest"
 "path/filepath"
 "strings"
 "testing"
)

func TestWorkOrderLookupAndPendingAddressEdit(t *testing.T) {
 cfg:=Config{DataDir:t.TempDir(),SessionKey:"test-secret-longer-than-24-chars",PriceFen:12900,Dev:true}
 store,err:=newStore(filepath.Join(cfg.DataDir,"store.json"))
 if err!=nil {t.Fatal(err)}
 app:=&App{db:store,cfg:cfg}
 mux:=app.routes()
 alice:=app.token("alice-wechat-id")
 bob:=app.token("bob-wechat-id")
 workID:="work-one"
 orderID:="order-one"
 if err:=store.update(func(d *Data) error {
  d.Works[workID]=&Work{ID:workID,Owner:"alice-wechat-id",Status:"model_ready",Model:"m.glb"}
  d.Orders[orderID]=&Order{ID:orderID,Owner:"alice-wechat-id",WorkID:workID,Name:"张三",Phone:"13800138000",Address:"广东省广州市旧地址100号",Status:"awaiting_payment",PriceFen:12900,CreatedAt:now()}
  d.Addresses["alice-wechat-id"]=&ShippingAddress{Owner:"alice-wechat-id",Name:"常用收件人",Phone:"13800138001",Address:"广东省广州市常用地址88号"}
  return nil
 });err!=nil{t.Fatal(err)}
 call:=func(method,path,token,body string)*httptest.ResponseRecorder{
  req:=httptest.NewRequest(method,path,strings.NewReader(body))
  req.Header.Set("Content-Type","application/json")
  if token!="" {req.Header.Set("Authorization","Bearer "+token)}
  out:=httptest.NewRecorder()
  mux.ServeHTTP(out,req)
  return out
 }
 var found struct{Order *Order `json:"order"`}
 mustJSON(t,call(http.MethodGet,"/api/works/"+workID+"/order",alice,""),200,&found)
 if found.Order==nil||found.Order.ID!=orderID||found.Order.Owner!=""||found.Order.Status!="awaiting_payment"{t.Fatalf("lookup bad: %+v",found)}
 mustJSON(t,call(http.MethodGet,"/api/works/"+workID+"/order",bob,""),404,nil)
 mustJSON(t,call(http.MethodGet,"/api/works/"+workID+"/order","",""),401,nil)
 mustJSON(t,call(http.MethodPost,"/api/orders",alice,`{"work_id":"work-one","name":"李四","phone":"13900139000","address":"广东省广州市另一个地址22号"}`),409,nil)

 endpoint:="/api/orders/"+orderID+"/shipping-address"
 payload:=`{"name":"李小红","phone":"13900139000","address":"广东省广州市海珠区新的收货地址200号"}`
 mustJSON(t,call(http.MethodPut,endpoint,"",payload),401,nil)
 mustJSON(t,call(http.MethodPut,endpoint,bob,payload),404,nil)
 mustJSON(t,call(http.MethodPut,endpoint,alice,`{"name":"a","phone":"2","address":"short"}`),400,nil)
 var updated Order
 mustJSON(t,call(http.MethodPut,endpoint,alice,payload),200,&updated)
 if updated.Name!="李小红"||updated.Owner!=""||updated.PriceFen!=12900||updated.WorkID!=workID{
  t.Fatalf("invalid order update: %+v",updated)
 }
 saved,ok:=store.order(orderID)
 if !ok||saved.Name!="李小红"||saved.Address!="广东省广州市海珠区新的收货地址200号"{t.Fatalf("not persisted: %+v",saved)}
 defaultAddr,ok:=store.address("alice-wechat-id")
 if !ok||defaultAddr.Name!="常用收件人"{t.Fatalf("order edit changed default address: %+v",defaultAddr)}
 reloaded,err:=newStore(filepath.Join(cfg.DataDir,"store.json"))
 if err!=nil{t.Fatal(err)}
 actual,ok:=reloaded.order(orderID)
 if !ok||actual.Phone!="13900139000"{t.Fatalf("order address missing after restart: %+v",actual)}

 if err:=store.update(func(d *Data)error{
  d.Orders[orderID].Status="paid"
  return nil
 });err!=nil{t.Fatal(err)}
 mustJSON(t,call(http.MethodPut,endpoint,alice,payload),409,nil)
 mustJSON(t,call(http.MethodGet,"/api/works/"+workID+"/order",alice,""),200,&found)
 if found.Order.Status!="paid"{t.Fatalf("paid status not exposed: %+v",found.Order)}
 if err:=store.update(func(d *Data)error{
  d.Orders[orderID].Status="canceled"
  return nil
 });err!=nil{t.Fatal(err)}
 mustJSON(t,call(http.MethodGet,"/api/works/"+workID+"/order",alice,""),200,&found)
 if found.Order!=nil{t.Fatalf("canceled order should not block checkout: %+v",found.Order)}
}
