package main

import (
 "crypto/aes"
 "crypto/cipher"
 "crypto/rand"
 "crypto/sha256"
 "encoding/base64"
 "errors"
 "fmt"
 "io"
 "net/http"
 "net/url"
 "sort"
 "strings"
)

// Each provider supplies one capability. Seedream is the direct image adapter;
// worker is a callback-based adapter usable for either image or 3D pipelines.
type AIProvider struct {
 ID string `json:"id"`
 Owner string `json:"owner"`
 CreatedAt string `json:"created_at"`
 Name string `json:"name"`
 Stage string `json:"stage"`
 Adapter string `json:"adapter"`
 Endpoint string `json:"endpoint"`
 Model string `json:"model"`
 Enabled bool `json:"enabled"`
 Default bool `json:"default"`
 APIKeyCipher string `json:"api_key_cipher,omitempty"`
}
type AIPrompt struct {
 ID string `json:"id"`
 Owner string `json:"owner"`
 CreatedAt string `json:"created_at"`
 Version int `json:"version"`
 Template string `json:"template"`
}
func defaultPrompt(stage string) string {
 if stage == "model" {
  return "将参考卡通形象转换为单个可3D打印的完整角色模型，保留脸部和服饰的核心辨识特征，结构完整稳定，避免悬浮部件、过薄的四肢和无法打印的细节；输出可预览的 GLB 模型。"
 }
 return "严格参考上传的儿童画，保留原画主体的轮廓、颜色、标志性装饰和稚拙特色，转换成单个全身、正面、立体感清晰的可爱卡通角色；纯净背景，造型简单完整，避免细小悬空零件，适合进一步生成3D打印白模。只输出一张图片，不添加文字、水印或额外角色。"
}
func (a *App) aiSnapshot(stage string) (*AIProvider, AIPrompt, error) {
 prompt := AIPrompt{ID:stage, Version:1, Template:defaultPrompt(stage)}
 var choice *AIProvider
 err := a.db.update(func(d *Data) error {
  if p:=d.AIPrompts[stage]; p!=nil { prompt=*p }
  for _,p:=range d.AIProviders {
   if p.Stage==stage && p.Enabled && p.Default { cp:=*p; choice=&cp; break }
  }
  return nil
 })
 return choice,prompt,err
}
func aiProviderView(p *AIProvider) map[string]any {
 return map[string]any{"id":p.ID,"name":p.Name,"stage":p.Stage,"adapter":p.Adapter,"endpoint":p.Endpoint,"model":p.Model,"enabled":p.Enabled,"default":p.Default,"has_key":p.APIKeyCipher!="","created_at":p.CreatedAt}
}
func (a *App) aiEncrypt(secret string) (string,error) {
 hash:=sha256.Sum256([]byte("tongyin-ai-api-key-v1:"+a.cfg.SessionKey))
 block,e:=aes.NewCipher(hash[:]); if e!=nil{return "",e}
 gcm,e:=cipher.NewGCM(block);if e!=nil{return "",e}
 nonce:=make([]byte,gcm.NonceSize());if _,e=io.ReadFull(rand.Reader,nonce);e!=nil{return "",e}
 return base64.RawStdEncoding.EncodeToString(append(nonce,gcm.Seal(nil,nonce,[]byte(secret),nil)...)),nil
}
func (a *App) aiDecrypt(ciphertext string) (string,error) {
 if ciphertext=="" { return "",errors.New("尚未配置 API Key") }
 raw,e:=base64.RawStdEncoding.DecodeString(ciphertext);if e!=nil{return "",e}
 hash:=sha256.Sum256([]byte("tongyin-ai-api-key-v1:"+a.cfg.SessionKey))
 block,e:=aes.NewCipher(hash[:]);if e!=nil{return "",e}
 gcm,e:=cipher.NewGCM(block);if e!=nil{return "",e}
 if len(raw)<gcm.NonceSize(){return "",errors.New("密钥存储损坏")}
 value,e:=gcm.Open(nil,raw[:gcm.NonceSize()],raw[gcm.NonceSize():],nil)
 if e!=nil{return "",errors.New("无法解密密钥：请检查 SESSION_SECRET 是否变化")}
 return string(value),nil
}
func validateAIProvider(p *AIProvider, dev bool) error {
 p.Name=strings.TrimSpace(p.Name)
 p.Endpoint=strings.TrimSpace(p.Endpoint)
 p.Model=strings.TrimSpace(p.Model)
 if p.Name=="" || len(p.Name)>60{return errors.New("名称不能为空且最多60字")}
 if p.Stage!="image" && p.Stage!="model"{return errors.New("仅支持 image 或 model")}
 if p.Adapter!="seedream" && p.Adapter!="worker"{return errors.New("不支持的适配器")}
 if p.Adapter=="seedream" && p.Stage!="image"{return errors.New("Seedream 只能生成图片")}
 if p.Model=="" && p.Adapter=="seedream"{return errors.New("请填写 Seedream 模型 ID")}
 if len(p.Model)>150{return errors.New("模型 ID 过长")}
 if p.Endpoint=="" && p.Adapter=="seedream" {p.Endpoint="https://ark.cn-beijing.volces.com/api/v3/images/generations"}
 u,e:=url.Parse(p.Endpoint)
 if e!=nil || u.Host=="" || u.User!=nil || u.Fragment!="" || u.RawQuery!="" {return errors.New("API 地址无效，不允许账号信息、查询参数或锚点")}
 if u.Scheme!="https" && !(dev && p.Adapter=="worker" && u.Scheme=="http"){return errors.New("正式环境仅允许 HTTPS API 地址")}
 if p.Adapter=="seedream" && (u.Scheme!="https" || u.Hostname()!="ark.cn-beijing.volces.com" || u.Path!="/api/v3/images/generations") {return errors.New("Seedream 请使用官方方舟图片生成 API 地址")}
 if len(p.Endpoint)>400{return errors.New("API 地址过长")}
 return nil
}
func (a *App) adminAIConfig(w http.ResponseWriter,r *http.Request) {
 if !a.admin(w,r){return}
 providers:=[]map[string]any{}
 prompts:=map[string]AIPrompt{"image":{ID:"image",Version:1,Template:defaultPrompt("image")},"model":{ID:"model",Version:1,Template:defaultPrompt("model")}}
 err:=a.db.update(func(d *Data)error{
  for _,p:=range d.AIProviders{providers=append(providers,aiProviderView(p))}
  for _,stage:=range []string{"image","model"} {if p:=d.AIPrompts[stage];p!=nil {prompts[stage]=*p}}
  return nil
 })
 if err!=nil{fail(w,500,"读取 AI 配置失败");return}
 sort.Slice(providers,func(i,j int)bool{return fmt.Sprint(providers[i]["created_at"])<fmt.Sprint(providers[j]["created_at"])})
 reply(w,200,map[string]any{"providers":providers,"prompts":prompts,"legacy_worker_enabled":a.cfg.AIWorkerURL!=""})
}
func (a *App) adminAISaveProvider(w http.ResponseWriter,r *http.Request) {
 if !a.admin(w,r){return}
 var q struct {
  ID string `json:"id"`
  Name string `json:"name"`
  Stage string `json:"stage"`
  Adapter string `json:"adapter"`
  Endpoint string `json:"endpoint"`
  Model string `json:"model"`
  APIKey string `json:"api_key"`
  Enabled bool `json:"enabled"`
  Default bool `json:"default"`
 }
 if decode(r,&q)!=nil {fail(w,400,"参数错误");return}
 p:=&AIProvider{Name:q.Name,Stage:q.Stage,Adapter:q.Adapter,Endpoint:q.Endpoint,Model:q.Model,Enabled:q.Enabled,Default:q.Default}
 if e:=validateAIProvider(p,a.cfg.Dev);e!=nil{fail(w,400,e.Error());return}
 if p.Default && !p.Enabled {fail(w,400,"默认模型必须处于启用状态");return}
 if len(q.APIKey)>4096{fail(w,400,"API Key 太长");return}
 if q.APIKey!="" {cipher,e:=a.aiEncrypt(strings.TrimSpace(q.APIKey));if e!=nil{fail(w,500,"加密密钥失败");return};p.APIKeyCipher=cipher}
 err:=a.db.update(func(d *Data)error{
  if d.AIProviders==nil {d.AIProviders=map[string]*AIProvider{}}
  if q.ID!="" {
   old,exists:=d.AIProviders[q.ID];if !exists{return errNotFound}
   if old.Stage!=p.Stage {return errors.New("已有提供商不能修改能力类型，请新建")}
   p.ID=old.ID;p.Owner=old.Owner;p.CreatedAt=old.CreatedAt
   if p.APIKeyCipher==""{p.APIKeyCipher=old.APIKeyCipher}
  } else {p.ID=newID();p.Owner="system";p.CreatedAt=now()}
  if p.Enabled && p.APIKeyCipher=="" {return errors.New("启用前请填写 API Key")}
  if p.Default {for _,x:=range d.AIProviders {if x.Stage==p.Stage {x.Default=false}}}
  d.AIProviders[p.ID]=p
  actor,_,_:=a.adminIdentity(r)
  recordAudit(d,actor,"ai.provider.saved",p.ID,p.Name+" / "+p.Stage)
  return nil
 })
 if errors.Is(err,errNotFound){fail(w,404,"提供商不存在");return}
 if err!=nil{fail(w,409,err.Error());return}
 reply(w,200,aiProviderView(p))
}
func (a *App) adminAIDeleteProvider(w http.ResponseWriter,r *http.Request) {
 if !a.admin(w,r){return}
 id:=r.PathValue("id")
 err:=a.db.update(func(d *Data)error{
  if _,ok:=d.AIProviders[id];!ok{return errNotFound}
  delete(d.AIProviders,id)
  actor,_,_:=a.adminIdentity(r);recordAudit(d,actor,"ai.provider.deleted",id,"")
  return nil
 })
 if errors.Is(err,errNotFound){fail(w,404,"提供商不存在");return}
 if err!=nil{fail(w,500,"删除提供商失败");return}
 reply(w,200,map[string]string{"status":"ok"})
}
func (a *App) adminAISavePrompt(w http.ResponseWriter,r *http.Request) {
 if !a.admin(w,r){return}
 stage:=r.PathValue("stage");if stage!="image"&&stage!="model"{fail(w,400,"stage 无效");return}
 var q struct {Template string `json:"template"`}
 if decode(r,&q)!=nil {fail(w,400,"参数错误");return}
 q.Template=strings.TrimSpace(q.Template)
 if len(q.Template)<10 || len(q.Template)>12000 {fail(w,400,"提示词长度须在10至12000字节之间");return}
 var updated AIPrompt
 err:=a.db.update(func(d *Data)error{
  if d.AIPrompts==nil {d.AIPrompts=map[string]*AIPrompt{}}
  p:=d.AIPrompts[stage]
  if p==nil {p=&AIPrompt{ID:stage,Owner:"system",CreatedAt:now()}}
  p.Version++
  p.Template=q.Template
  d.AIPrompts[stage]=p;updated=*p
  actor,_,_:=a.adminIdentity(r);recordAudit(d,actor,"ai.prompt.saved",stage,fmt.Sprintf("v%d",p.Version))
  return nil
 })
 if err!=nil{fail(w,500,"保存提示词失败");return}
 reply(w,200,updated)
}
