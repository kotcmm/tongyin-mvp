package main

import (
 "net/http/httptest"
 "path/filepath"
 "strings"
 "testing"
)
func TestCustomerCancelOrderAndPaymentRace(t *testing.T){
 cfg:=Config{SessionKey:"test-secret-longer-than-24-chars",AdminToken:"legacy-admin",Dev:true}
 db,err:=newStore(filepath.Join(t.TempDir(),"data.json"));if err!=nil{t.Fatal(err)}
 a:=&App{cfg:cfg,db:db}
 err=db.update(func(d *Data)error{
  d.Orders["pending"]=&Order{ID:"pending",WorkID:"w1",Owner:"alice",Status:"awaiting_payment"}
  d.Orders["pay-attempt"]=&Order{ID:"pay-attempt",WorkID:"w2",Owner:"alice",Status:"awaiting_payment",PaymentStartedAt:now()}
  return nil
 });if err!=nil{t.Fatal(err)}
 call:=func(id,token string,admin bool)*httptest.ResponseRecorder{
  r:=httptest.NewRequest("POST","/api/orders/"+id+"/cancel",strings.NewReader(`{"reason":"不需要这件模型"}`))
  if token!=""{r.Header.Set("Authorization","Bearer "+token)}
  if admin{r.Header.Set("X-Admin-Token",cfg.AdminToken)}
  w:=httptest.NewRecorder();a.routes().ServeHTTP(w,r);return w
 }
 mustJSON(t,call("pending",a.token("bob"),false),404,nil)
 mustJSON(t,call("pay-attempt",a.token("alice"),false),409,nil)
 mustJSON(t,call("pending",a.token("alice"),false),200,nil)
 pending,_:=db.order("pending")
 if pending.Status!="canceled"||len(pending.History)!=1{t.Fatalf("user canceled order improperly: %+v",pending)}
 mustJSON(t,call("pending",a.token("alice"),false),409,nil)
 // Administrative cancel also rejects payment attempts.
 r:=httptest.NewRequest("POST","/api/admin/orders/pay-attempt/status",strings.NewReader(`{"Status":"canceled"}`))
 r.Header.Set("X-Admin-Token",cfg.AdminToken)
 w:=httptest.NewRecorder();a.routes().ServeHTTP(w,r)
 mustJSON(t,w,409,nil)
 still,_:=db.order("pay-attempt")
 if still.Status!="awaiting_payment"{t.Fatal("payment attempt canceled despite guard")}
}
