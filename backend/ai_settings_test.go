package main

import (
 "bytes"
 "encoding/json"
 "net/http"
 "net/http/httptest"
 "os"
 "path/filepath"
 "strings"
 "testing"
)

func aiTestApp(t *testing.T) (*App,http.Handler){
 t.Helper()
 dir:=t.TempDir()
 db,err:=newStore(filepath.Join(dir,"store.json"));if err!=nil{t.Fatal(err)}
 a:=&App{cfg:Config{DataDir:dir,PublicURL:"http://localhost:8080",AdminToken:"admin-secret",SessionKey:"a-long-stable-session-secret-for-tests",Dev:true},db:db}
 return a,a.routes()
}
func aiAdminReq(t *testing.T, handler http.Handler,method,path string,v any) *httptest.ResponseRecorder {
 t.Helper()
 var body []byte
 if v!=nil{body,_=json.Marshal(v)}
 req:=httptest.NewRequest(method,path,bytes.NewReader(body))
 req.Header.Set("X-Admin-Token","admin-secret")
 req.Header.Set("Content-Type","application/json")
 res:=httptest.NewRecorder();handler.ServeHTTP(res,req)
 return res
}
func TestAIProviderSettingsAndPrompt(t *testing.T){
 a,mux:=aiTestApp(t)
 key:="keep-me-private-123"
 create:=map[string]any{"name":"Seedream","stage":"image","adapter":"seedream","endpoint":"https://ark.cn-beijing.volces.com/api/v3/images/generations","model":"doubao-seedream-4-5-251128","api_key":key,"enabled":true,"default":true}
 resp:=aiAdminReq(t,mux,"POST","/api/admin/ai/providers",create)
 if resp.Code!=200{t.Fatalf("create: %d %s",resp.Code,resp.Body.String())}
 if strings.Contains(resp.Body.String(),key) || strings.Contains(resp.Body.String(),"api_key_cipher"){t.Fatal("credential leaked in creation response")}
 var item struct{ID string `json:"id"`;HasKey bool `json:"has_key"`}
 if err:=json.Unmarshal(resp.Body.Bytes(),&item);err!=nil||item.ID==""||!item.HasKey{t.Fatal("missing created provider",err)}
 read:=aiAdminReq(t,mux,"GET","/api/admin/ai/config",nil)
 if read.Code!=200 || strings.Contains(read.Body.String(),key) || strings.Contains(read.Body.String(),"api_key_cipher"){t.Fatal("configuration leaked secret")}
 p,pr,err:=a.aiSnapshot("image")
 if err!=nil||p==nil||pr.Version!=1||p.ID!=item.ID{t.Fatal("default provider unavailable",err)}
 secret,err:=a.aiDecrypt(p.APIKeyCipher)
 if err!=nil||secret!=key{t.Fatal("decryption failed",err)}
 prompt:=aiAdminReq(t,mux,"PUT","/api/admin/ai/prompts/image",map[string]string{"template":"保留儿童画主体轮廓与标志性细节，生成一个便于打印的角色。"})
 if prompt.Code!=200{t.Fatal(prompt.Body.String())}
 _,pr,err=a.aiSnapshot("image")
 if err!=nil||pr.Version!=1||!strings.Contains(pr.Template,"儿童画"){t.Fatal("prompt not persisted",err)}
 create["id"]=item.ID;create["api_key"]=""
 create["name"]="Seedream Updated"
 resp=aiAdminReq(t,mux,"POST","/api/admin/ai/providers",create)
 if resp.Code!=200{t.Fatal(resp.Body.String())}
 p,_,_=a.aiSnapshot("image")
 secret,err=a.aiDecrypt(p.APIKeyCipher);if err!=nil||secret!=key{t.Fatal("empty key should retain credential")}
 deleteResp:=aiAdminReq(t,mux,"DELETE","/api/admin/ai/providers/"+item.ID,nil)
 if deleteResp.Code!=200{t.Fatal(deleteResp.Body.String())}
 p,_,_=a.aiSnapshot("image");if p!=nil{t.Fatal("deleted default is still selected")}
}
func TestAISecretsSurviveRestartAndRoleGuard(t *testing.T){
 a,mux:=aiTestApp(t)
 data:=map[string]any{"name":"3D Worker","stage":"model","adapter":"worker","endpoint":"https://worker.example.com/callback","model":"","api_key":"secret-worker-key","enabled":true,"default":true}
 res:=aiAdminReq(t,mux,"POST","/api/admin/ai/providers",data)
 if res.Code!=200{t.Fatal(res.Body.String())}
 dir:=a.cfg.DataDir
 again,err:=newStore(filepath.Join(dir,"store.json"));if err!=nil{t.Fatal(err)}
 a.db=again
 p,_,err:=a.aiSnapshot("model")
 if err!=nil||p==nil{t.Fatal("stored provider missing",err)}
 secret,err:=a.aiDecrypt(p.APIKeyCipher);if err!=nil||secret!="secret-worker-key"{t.Fatal("encrypted key not persistent")}
 bad:=httptest.NewRecorder()
 mux.ServeHTTP(bad,httptest.NewRequest("GET","/api/admin/ai/config",nil))
 if bad.Code!=401{t.Fatalf("unauthenticated read accepted %d",bad.Code)}
 _=os.Remove(filepath.Join(dir,"nonexistent"))
}
func TestAIProviderValidation(t *testing.T){
 for _,p:=range []*AIProvider{
  {Name:"wrong",Stage:"model",Adapter:"seedream",Endpoint:"https://ark.cn-beijing.volces.com/api/v3/images/generations",Model:"m"},
  {Name:"unsafe",Stage:"image",Adapter:"seedream",Endpoint:"https://malicious.example.com/steal",Model:"m"},
  {Name:"unsafe",Stage:"model",Adapter:"worker",Endpoint:"http://worker.example.com/job"},
 }{
  if err:=validateAIProvider(p,false);err==nil{t.Fatalf("invalid provider accepted: %#v",p)}
 }
}
