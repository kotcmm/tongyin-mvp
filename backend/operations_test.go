package main

import(
 "bytes"
 "encoding/json"
 "fmt"
 "io"
 "net/http"
 "net/http/httptest"
 "path/filepath"
 "strings"
 "testing"
)

func TestSingleServiceConsoleAndAdminRoles(t *testing.T){
 cfg:=Config{SessionKey:"test-secret-longer-than-24-chars",AdminUsername:"operator",AdminPassword:"a-strong-password-2026",AdminToken:"legacy-root",Dev:true}
 db,err:=newStore(filepath.Join(t.TempDir(),"db.json"))
 if err!=nil{t.Fatal(err)}
 if err:=bootstrapAdmin(db,cfg);err!=nil{t.Fatal(err)}
 a:=&App{cfg:cfg,db:db}
 mux:=a.routes()
 get:=func(method,path,token,body string)*httptest.ResponseRecorder{
  req:=httptest.NewRequest(method,path,strings.NewReader(body))
  if token!=""{req.Header.Set("Authorization","Bearer "+token)}
  req.Header.Set("Content-Type","application/json")
  rec:=httptest.NewRecorder();mux.ServeHTTP(rec,req);return rec
 }
 root:=get("GET","/admin/","","")
 if root.Code!=200 || !strings.Contains(root.Body.String(),"运营管理中心")||!strings.Contains(root.Body.String(),"物流管理"){
  t.Fatalf("embedded single-binary admin console missing: %d",root.Code)
 }
 var logged struct{Token string `json:"token"`;Role string `json:"role"`}
 mustJSON(t,get("POST","/api/admin/auth/login","",`{"username":"operator","password":"a-strong-password-2026"}`),200,&logged)
 if logged.Role!="owner"||logged.Token==""{t.Fatalf("bootstrap login invalid: %+v",logged)}
 mustJSON(t,get("GET","/api/admin/dashboard",logged.Token,""),200,nil)
 var staff struct{Username string `json:"username"`}
 mustJSON(t,get("POST","/api/admin/accounts",logged.Token,`{"username":"maker","password":"maker-password-long","role":"production"}`),201,&staff)
 if staff.Username!="maker"{t.Fatal(staff)}
 var production struct{Token string `json:"token"`}
 mustJSON(t,get("POST","/api/admin/auth/login","",`{"username":"maker","password":"maker-password-long"}`),200,&production)
 mustJSON(t,get("GET","/api/admin/works",production.Token,""),200,nil)
 mustJSON(t,get("GET","/api/admin/accounts",production.Token,""),403,nil)
 mustJSON(t,get("POST","/api/admin/accounts",production.Token,`{"username":"intruder","password":"really-long-password","role":"owner"}`),403,nil)
 mustJSON(t,get("POST","/api/admin/auth/login","",`{"username":"operator","password":"wrong-password"}`),401,nil)
 mustJSON(t,get("PUT","/api/admin/accounts/maker",logged.Token,`{"disabled":true}`),200,nil)
 mustJSON(t,get("GET","/api/admin/works",production.Token,""),401,nil)
}
func TestWeChatUserIsolationAndLegacyUpgrade(t *testing.T){
 cfg:=Config{SessionKey:"test-secret-longer-than-24-chars",Dev:true}
 db,err:=newStore(filepath.Join(t.TempDir(),"data.json"));if err!=nil{t.Fatal(err)}
 if err:=db.update(func(d *Data)error{
  d.Works["legacy-work"]=&Work{ID:"legacy-work",Owner:"legacy-user",CreatedAt:now(),Status:"model_ready",Model:"a.glb"}
  return nil
 });err!=nil{t.Fatal(err)}
 if err:=backfillLegacyUsers(db);err!=nil{t.Fatal(err)}
 older,ok:=db.user("legacy-user")
 if !ok || older.ID==""{t.Fatal("legacy work owner not upgraded")}
 a:=&App{cfg:cfg,db:db}
 mux:=a.routes()
 req:=func(path,token string)*httptest.ResponseRecorder{
  r:=httptest.NewRequest("GET",path,nil)
  r.Header.Set("Authorization","Bearer "+token)
  w:=httptest.NewRecorder();mux.ServeHTTP(w,r);return w
 }
 aToken,bToken:=a.token("legacy-user"),a.token("other-user")
 var me map[string]any
 mustJSON(t,req("/api/me",aToken),200,&me)
 if me["id"]!=older.ID||me["works"].(float64)!=1{t.Fatalf("user record mismatch: %v",me)}
 mustJSON(t,req("/api/works/legacy-work",bToken),404,nil)
 mustJSON(t,req("/api/works/legacy-work",aToken),200,nil)
 if _,exists:=db.user("other-user");!exists{t.Fatal("new session did not register user")}
}

type fakeCourierTransport struct{received string}
func (f *fakeCourierTransport) RoundTrip(r *http.Request)(*http.Response,error){
 if r.URL.Host!="poll.kuaidi100.com"||r.Method!="POST"{return nil,fmt.Errorf("wrong courier endpoint")}
 raw,err:=io.ReadAll(r.Body);if err!=nil{return nil,err}
 f.received=string(raw)
 data:=`{"status":"200","message":"ok","state":"3","ischeck":"1","data":[{"time":"2026-10-14 09:22:00","context":"您的快件已签收"},{"time":"2026-10-13 18:30:00","context":"已揽收"}]}`
 return &http.Response{StatusCode:200,Body:io.NopCloser(bytes.NewBufferString(data)),Header:make(http.Header)},nil
}
func TestShippingTrackingAndOwnership(t *testing.T){
 cfg:=Config{SessionKey:"test-secret-longer-than-24-chars",AdminToken:"secret-admin-token",KuaidiCustomer:"test-customer",KuaidiKey:"test-key",Dev:true}
 db,err:=newStore(filepath.Join(t.TempDir(),"data.json"));if err!=nil{t.Fatal(err)}
 a:=&App{cfg:cfg,db:db}
 fake:=&fakeCourierTransport{}
 a.client=&http.Client{Transport:fake}
 if err:=db.update(func(d *Data)error{
  d.Orders["order-a"]=&Order{ID:"order-a",Owner:"alice",Status:"printing",CreatedAt:now(),Phone:"13800138000"}
  return nil
 });err!=nil{t.Fatal(err)}
 mux:=a.routes()
 call:=func(method,path,token,body string,admin bool)*httptest.ResponseRecorder{
  req:=httptest.NewRequest(method,path,strings.NewReader(body))
  req.Header.Set("Content-Type","application/json")
  if admin{req.Header.Set("X-Admin-Token",cfg.AdminToken)}
  if token!=""{req.Header.Set("Authorization","Bearer "+token)}
  w:=httptest.NewRecorder();mux.ServeHTTP(w,req);return w
 }
 endpoint:="/api/admin/orders/order-a/shipment"
 payload:=`{"carrier":"顺丰速运","carrier_code":"shunfeng","tracking":"SF12345678901"}`
 mustJSON(t,call("POST",endpoint,"",payload,false),401,nil)
 mustJSON(t,call("POST",endpoint,"",payload,true),200,nil)
 mustJSON(t,call("POST",endpoint,"",payload,true),409,nil)
 mustJSON(t,call("GET","/api/orders/order-a/tracking",a.token("bob"),"",false),404,nil)
 var tracking map[string]any
 mustJSON(t,call("GET","/api/orders/order-a/tracking",a.token("alice"),"",false),200,&tracking)
 if tracking["tracking_no"]!="SF12345678901"{t.Fatal(tracking)}
 mustJSON(t,call("POST","/api/admin/orders/order-a/tracking/refresh","",`{}`,true),200,nil)
 if !strings.Contains(fake.received,"customer=")||!strings.Contains(fake.received,"sign="){t.Fatalf("signed courier request absent: %s",fake.received)}
 mustJSON(t,call("POST","/api/admin/orders/order-a/tracking/refresh","",`{}`,true),429,nil)
 mustJSON(t,call("GET","/api/orders/order-a/tracking",a.token("alice"),"",false),200,&tracking)
 if tracking["tracking_state"]!="delivered"{t.Fatalf("not marked delivered: %v",tracking)}
 events,ok:=tracking["events"].([]any);if !ok||len(events)!=2{t.Fatalf("tracking events lost: %v",tracking)}
 var audit []AuditRecord
 mustJSON(t,call("GET","/api/admin/audit","", "",true),200,&audit)
 if len(audit)<2{t.Fatalf("missing audit records: %+v",audit)}
 // Completing does not alter the courier records.
 mustJSON(t,call("POST","/api/admin/orders/order-a/status","",`{"Status":"completed"}`,true),200,nil)
 saved,_:=db.order("order-a")
 if saved.Status!="completed"||len(saved.TrackingEvents)!=2{t.Fatalf("completion changed tracking: %+v",saved)}
}
func TestPaymentStatusIsNotInventedByRefundRequest(t *testing.T){
 cfg:=Config{SessionKey:"test-secret-longer-than-24-chars",AdminToken:"admin-token"}
 db,err:=newStore(filepath.Join(t.TempDir(),"data.json"));if err!=nil{t.Fatal(err)}
 db.update(func(d *Data)error{
  d.Orders["paid-order"]=&Order{ID:"paid-order",Owner:"alice",Status:"paid",PriceFen:12900};return nil
 })
 a:=&App{cfg:cfg,db:db}
 req:=httptest.NewRequest("POST","/api/admin/orders/paid-order/refund",strings.NewReader(`{"reason":"用户提出退款要求"}`))
 req.Header.Set("X-Admin-Token",cfg.AdminToken)
 w:=httptest.NewRecorder();a.routes().ServeHTTP(w,req);mustJSON(t,w,200,nil)
 order,_:=db.order("paid-order")
 if order.Status!="paid"||order.RefundStatus!="requested"{t.Fatalf("refund request improperly changed paid status: %+v",order)}
}
func TestUserRecordJSONPreservesOriginalAssets(t *testing.T){
 d:=Data{Works:map[string]*Work{"w":{ID:"w",Owner:"legacy",Original:"drawing.png",Model:"print.glb"}},Orders:map[string]*Order{},Users:map[string]*User{}}
 raw,err:=json.Marshal(d);if err!=nil{t.Fatal(err)}
 var decoded Data
 if json.Unmarshal(raw,&decoded)!=nil||decoded.Works["w"].Model!="print.glb"{t.Fatal("legacy asset lost")}
}
