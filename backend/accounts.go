package main

import (
 "crypto/hmac"
 "crypto/rand"
 "crypto/sha256"
 "crypto/subtle"
 "encoding/base64"
 "encoding/hex"
 "errors"
 "fmt"
 "net/http"
 "strconv"
 "strings"
 "time"
)

const adminIterations = 120000

func passwordHash(password, salt string) string {
 key:=[]byte(password)
 block:=append([]byte(salt),0,0,0,1)
 mac:=hmac.New(sha256.New,key)
 mac.Write(block)
 u:=mac.Sum(nil)
 out:=append([]byte(nil),u...)
 for i:=1;i<adminIterations;i++{
  mac=hmac.New(sha256.New,key)
  mac.Write(u)
  u=mac.Sum(nil)
  for n:=range out{out[n]^=u[n]}
 }
 return hex.EncodeToString(out)
}
func newAdmin(name, password, role string) (*AdminUser,error) {
 if len(password)<12{return nil,errors.New("后台密码至少12位")}
 name=strings.TrimSpace(name)
 if len(name)<3||len(name)>32||strings.ContainsAny(name," \t\n|/"){return nil,errors.New("账号格式不正确")}
 switch role{
 case "owner","operator","production","shipping","service":
 default:return nil,errors.New("无效角色")
 }
 saltBytes:=make([]byte,16)
 if _,err:=rand.Read(saltBytes);err!=nil{return nil,err}
 salt:=hex.EncodeToString(saltBytes)
 return &AdminUser{Owner:name,CreatedAt:now(),Role:role,Salt:salt,PasswordHash:passwordHash(password,salt),Version:1},nil
}
func bootstrapAdmin(db Repository, cfg Config) error{
 // No automatic default credentials: first deployment must set an explicit password.
 if cfg.AdminPassword=="" {return nil}
 name:=cfg.AdminUsername
 if name==""{name="admin"}
 if _,exists:=db.adminUser(name);exists{return nil}
 u,err:=newAdmin(name,cfg.AdminPassword,"owner")
 if err!=nil{return fmt.Errorf("后台初始账号：%w",err)}
 return db.update(func(d *Data)error{
  if d.Admins==nil{d.Admins=map[string]*AdminUser{}}
  if _,found:=d.Admins[name];!found{d.Admins[name]=u}
  return nil
 })
}
func (a *App) adminToken(u *AdminUser) string {
 exp:=strconv.FormatInt(time.Now().Add(12*time.Hour).Unix(),10)
 data:=base64.RawURLEncoding.EncodeToString([]byte(u.Owner+"|"+strconv.Itoa(u.Version)+"|"+exp))
 mac:=hmac.New(sha256.New,[]byte(a.cfg.SessionKey))
 mac.Write([]byte("admin1."+data))
 return "admin1."+data+"."+base64.RawURLEncoding.EncodeToString(mac.Sum(nil))
}
func (a *App) adminIdentity(r *http.Request) (string,string,bool) {
 // Old API token remains an explicit emergency compatibility credential.
 // Do not display it in any API response or use it for ordinary staff accounts.
 if a.isAdmin(r){return "legacy-token","owner",true}
 value:=strings.TrimPrefix(r.Header.Get("Authorization"),"Bearer ")
 p:=strings.Split(value,".")
 if len(p)!=3||p[0]!="admin1"{return "","",false}
 mac:=hmac.New(sha256.New,[]byte(a.cfg.SessionKey))
 mac.Write([]byte("admin1."+p[1]))
 sig,err:=base64.RawURLEncoding.DecodeString(p[2])
 if err!=nil||!hmac.Equal(mac.Sum(nil),sig){return "","",false}
 raw,err:=base64.RawURLEncoding.DecodeString(p[1])
 if err!=nil{return "","",false}
 v:=strings.Split(string(raw),"|")
 if len(v)!=3{return "","",false}
 ver,err1:=strconv.Atoi(v[1])
 exp,err2:=strconv.ParseInt(v[2],10,64)
 if err1!=nil||err2!=nil||exp<=time.Now().Unix(){return "","",false}
 user,ok:=a.db.adminUser(v[0])
 if !ok||user.Disabled||user.Version!=ver{return "","",false}
 return user.Owner,user.Role,true
}
func adminAllowed(role string,r *http.Request) bool{
 if role=="owner"{return true}
 path:=r.URL.Path
 method:=r.Method
 if method=="GET" {
  if strings.HasPrefix(path,"/api/admin/dashboard")||strings.HasPrefix(path,"/api/admin/works")||strings.HasPrefix(path,"/api/admin/orders")||strings.HasPrefix(path,"/api/admin/me"){return true}
  if role=="operator"||role=="service"{return strings.HasPrefix(path,"/api/admin/users")}
  return false
 }
 if role=="operator"||role=="production"{
  if method=="POST"&&strings.HasPrefix(path,"/api/admin/works/"){return true}
 }
 if role=="operator"||role=="shipping"{
  if method=="POST"&&strings.HasPrefix(path,"/api/admin/orders/"){return true}
 }
 return false
}
func (a *App) adminLogin(w http.ResponseWriter,r *http.Request){
 var q struct{ Username string `json:"username"`; Password string `json:"password"`}
 if decode(r,&q)!=nil {fail(w,400,"参数错误");return}
 u,found:=a.db.adminUser(q.Username)
 if !found||u.Disabled{fail(w,401,"账号或密码错误");return}
 actual:=passwordHash(q.Password,u.Salt)
 if subtle.ConstantTimeCompare([]byte(actual),[]byte(u.PasswordHash))!=1{
  fail(w,401,"账号或密码错误");return
 }
 reply(w,200,map[string]any{"token":a.adminToken(u),"username":u.Owner,"role":u.Role})
}
func (a *App) adminMe(w http.ResponseWriter,r *http.Request){
 name,role,ok:=a.adminIdentity(r)
 if !ok{fail(w,401,"后台登录已失效");return}
 reply(w,200,map[string]string{"username":name,"role":role})
}
func (a *App) adminAccounts(w http.ResponseWriter,r *http.Request){
 if !a.admin(w,r){return}
 if r.Method=="GET" {
  result:=[]map[string]any{}
  err:=a.db.update(func(d *Data)error{
   for _,u:=range d.Admins{result=append(result,map[string]any{"username":u.Owner,"role":u.Role,"disabled":u.Disabled,"created_at":u.CreatedAt})}
   return nil
  })
  if err!=nil{fail(w,500,"读取管理员失败");return}
  reply(w,200,result);return
 }
 if r.Method!="POST"{fail(w,405,"Method not allowed");return}
 var q struct{Username string `json:"username"`;Password string `json:"password"`;Role string `json:"role"`}
 if decode(r,&q)!=nil{fail(w,400,"参数错误");return}
 u,err:=newAdmin(q.Username,q.Password,q.Role)
 if err!=nil{fail(w,400,err.Error());return}
 err=a.db.update(func(d *Data)error{
  if _,exists:=d.Admins[u.Owner];exists{return errors.New("账号已存在")}
  d.Admins[u.Owner]=u
  recordAudit(d,"system","admin.created",u.Owner,u.Role)
  return nil
 })
 if err!=nil{fail(w,409,err.Error());return}
 reply(w,201,map[string]any{"username":u.Owner,"role":u.Role})
}
func (a *App) adminAccountUpdate(w http.ResponseWriter,r *http.Request){
 if !a.admin(w,r){return}
 name:=r.PathValue("id")
 var q struct{Role string `json:"role"`;Password string `json:"password"`;Disabled *bool `json:"disabled"`}
 if decode(r,&q)!=nil{fail(w,400,"参数错误");return}
 if q.Role!=""{
  switch q.Role{case "owner","operator","production","shipping","service":default:fail(w,400,"角色不存在");return}
 }
 err:=a.db.update(func(d *Data)error{
  a,ok:=d.Admins[name];if !ok{return errNotFound}
  if a.Role=="owner" && ((q.Role!="" && q.Role!="owner")||(q.Disabled!=nil && *q.Disabled)){
   owners:=0
   for _,p:=range d.Admins{if p.Role=="owner"&&!p.Disabled{owners++}}
   if owners<=1{return errors.New("至少保留一个可用的超级管理员")}
  }
  if q.Role!=""{a.Role=q.Role}
  if q.Disabled!=nil{a.Disabled=*q.Disabled}
  if q.Password!=""{
   fresh,err:=newAdmin(name,q.Password,a.Role);if err!=nil{return err}
   a.Salt=fresh.Salt;a.PasswordHash=fresh.PasswordHash
  }
  a.Version++ // invalidate all previous sessions on any account change
  recordAudit(d,"system","admin.updated",name,"")
  return nil
 })
 if errors.Is(err,errNotFound){fail(w,404,"管理员不存在");return}
 if err!=nil{fail(w,409,err.Error());return}
 reply(w,200,map[string]string{"status":"ok"})
}
func (a *App) ensureUser(owner string) error{
 if owner==""{return errors.New("invalid identity")}
 if u,exists:=a.db.user(owner);exists{
  if u.Disabled{return errors.New("账号已停用")}
  return nil
 }
 return a.db.update(func(d *Data)error{
  if d.Users==nil{d.Users=map[string]*User{}}
  if existing,found:=d.Users[owner];found{
   if existing.Disabled{return errors.New("账号已停用")}
   return nil
  }
  d.Users[owner]=&User{ID:newID(),Owner:owner,CreatedAt:now(),LastLogin:now()}
  return nil
 })
}
func (a *App) customerMe(w http.ResponseWriter,r *http.Request){
 owner:=a.uid(w,r);if owner==""{return}
 u,ok:=a.db.user(owner)
 if !ok{fail(w,500,"用户信息不可用");return}
 var works,orders int
 for range a.db.works(owner,false){works++}
 for range a.db.orders(owner,false){orders++}
 reply(w,200,map[string]any{"id":u.ID,"created_at":u.CreatedAt,"works":works,"orders":orders})
}
func recordAudit(d *Data,actor,action,target,detail string){
 if d.Audit==nil{d.Audit=map[string]*AuditRecord{}}
 id:=newID()
 d.Audit[id]=&AuditRecord{ID:id,Owner:actor,CreatedAt:now(),Action:action,Target:target,Detail:detail}
}

func checkAdminReady(db Repository,c Config) error{
 if c.AdminToken!=""{return nil}
 count:=0
 err:=db.update(func(d *Data)error{
  for _,v:=range d.Admins{if !v.Disabled{count++}}
  return nil
 })
 if err!=nil{return err}
 if count==0{return errors.New("请配置 ADMIN_BOOTSTRAP_PASSWORD（至少12位）建立首个管理员账号")}
 return nil
}
func (a *App) trackLogin(owner string) error{
 return a.db.update(func(d *Data)error{
  u,exists:=d.Users[owner]
  if !exists{return errors.New("用户档案不存在")}
  if u.Disabled{return errors.New("账号已停用")}
  u.LastLogin=now()
  return nil
 })
}
