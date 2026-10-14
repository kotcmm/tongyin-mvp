package main

import (
 "encoding/json"
 "fmt"
 "net/http"
 "net/http/httptest"
 "net/url"
 "path/filepath"
 "strings"
 "testing"
)

func TestAdminServerSidePaginationSearchAndSecurity(t *testing.T){
 db,err:=newStore(filepath.Join(t.TempDir(),"data.json"))
 if err!=nil{t.Fatal(err)}
 if err:=db.update(func(d *Data)error{
  for i:=0;i<131;i++{
   owner:=fmt.Sprintf("openid-private-%03d",i)
   uid:=fmt.Sprintf("user-%03d",i)
   d.Users[owner]=&User{Owner:owner,ID:uid,CreatedAt:fmt.Sprintf("2026-10-14T09:%02d:00+08:00",i%60)}
   status:="image_pending"
   if i%2==0{status="model_ready"}
   workID:=fmt.Sprintf("w%03d",i)
   d.Works[workID]=&Work{ID:workID,Owner:owner,Status:status,CreatedAt:fmt.Sprintf("2026-10-14T09:%02d:00+08:00",i%60)}
   orderStatus:="awaiting_payment"
   if i%2==0{orderStatus="shipped"}
   d.Orders[fmt.Sprintf("o%03d",i)]=&Order{
    ID:fmt.Sprintf("o%03d",i),Owner:owner,WorkID:workID,
    Status:orderStatus,Name:fmt.Sprintf("客户-%03d",i),
    Phone:fmt.Sprintf("1380013%04d",i),Tracking:fmt.Sprintf("SF%09d",i),
    CreatedAt:fmt.Sprintf("2026-10-14T09:%02d:00+08:00",i%60),
   }
  }
  return nil
 });err!=nil{t.Fatal(err)}
 a:=&App{cfg:Config{SessionKey:"test-secret-longer-than-24-chars",AdminToken:"admin-legacy"},db:db}
 mux:=a.routes()
 call:=func(route,token string)*httptest.ResponseRecorder{
  req:=httptest.NewRequest(http.MethodGet,route,nil)
  if token!=""{req.Header.Set("X-Admin-Token",token)}
  w:=httptest.NewRecorder()
  mux.ServeHTTP(w,req)
  return w
 }
 mustJSON(t,call("/api/admin/search/works",""),401,nil)
 var works AdminPageResult[*Work]
 mustJSON(t,call("/api/admin/search/works?page=2&page_size=10","admin-legacy"),200,&works)
 if works.Total!=131||works.Page!=2||len(works.Items)!=10 {t.Fatalf("incorrect server page: %+v",works)}
 if works.Items[0].Owner!=""||works.Items[0].UserID==""{t.Fatalf("private owner leaked or user missing: %+v",works.Items[0])}
 var one AdminPageResult[*Work]
 mustJSON(t,call("/api/admin/search/works?q=w010&page_size=5","admin-legacy"),200,&one)
 if one.Total!=1||len(one.Items)!=1||one.Items[0].ID!="w010"{t.Fatalf("work search wrong: %+v",one)}
 mustJSON(t,call("/api/admin/search/works?status=model_ready&page_size=1","admin-legacy"),200,&one)
 if one.Total!=66||len(one.Items)!=1{t.Fatalf("work status not server filtered: %+v",one)}
 var orders AdminPageResult[*Order]
 mustJSON(t,call("/api/admin/search/orders?category=shipping&page_size=7","admin-legacy"),200,&orders)
 if orders.Total!=66||len(orders.Items)!=7{t.Fatalf("shipping category incorrect: %+v",orders)}
 for _,o:=range orders.Items{
  if o.Owner!=""||o.Status!="shipped"{t.Fatalf("order leak/mismatch: %+v",o)}
 }
 mustJSON(t,call("/api/admin/search/orders?q="+url.QueryEscape("客户-019"),"admin-legacy"),200,&orders)
 if orders.Total!=1||orders.Items[0].ID!="o019"{t.Fatalf("Chinese search wrong: %+v",orders)}
 mustJSON(t,call("/api/admin/search/orders?q="+url.QueryEscape("SF000000021"),"admin-legacy"),200,&orders)
 if orders.Total!=1||orders.Items[0].ID!="o021"{t.Fatalf("tracking search wrong: %+v",orders)}
 var users AdminPageResult[AdminUserRow]
 mustJSON(t,call("/api/admin/search/users?q=user-025","admin-legacy"),200,&users)
 if users.Total!=1||users.Items[0].ID!="user-025"||users.Items[0].Orders!=1||users.Items[0].Works!=1{
  t.Fatalf("user query/count incorrect: %+v",users)
 }
 result:=call("/api/admin/search/users?page=10000&page_size=12","admin-legacy")
 mustJSON(t,result,200,&users)
 if users.Total!=131||len(users.Items)!=0 {t.Fatal("invalid empty out-of-range page")}
 for _,bad:=range []string{"page=0","page=nan","page_size=101","page=-5"}{
  mustJSON(t,call("/api/admin/search/works?"+bad,"admin-legacy"),400,nil)
 }
 tooLong:=strings.Repeat("a",101)
 mustJSON(t,call("/api/admin/search/works?q="+tooLong,"admin-legacy"),400,nil)
 encoded,_:=json.Marshal(works)
 if strings.Contains(string(encoded),"openid-private") {t.Fatal("private OpenID returned in admin search")}
}
func TestAdminSearchSQLGenerationUsesBoundedQueries(t *testing.T){
 for _,dialect:=range []string{"postgres","mysql"}{
  s:=&SQLStore{dialect:dialect}
  if got:=s.bind("SELECT t.payload FROM ty_works t WHERE t.id=? LIMIT ? OFFSET ?"); dialect=="postgres"&&got!="SELECT t.payload FROM ty_works t WHERE t.id=$1 LIMIT $2 OFFSET $3"{
   t.Fatalf("postgres parameter binding failed: %s",got)
  }
  expr:=sqlStatusExpr(dialect,"t")
  if !strings.Contains(expr,"status")||!strings.Contains(sqlFieldExpr(dialect,"u","id"),"id"){t.Fatal("wrong JSON expressions")}
 }
}
