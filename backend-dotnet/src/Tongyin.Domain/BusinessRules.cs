using System.Text.Json.Nodes;

namespace Tongyin.Api.Core;

// Legacy JSON is deliberately preserved for exact Go data compatibility.
// Domain rules are pure: no storage, HTTP requests, sessions or external APIs.
public static class WorkRules
{
    public static void RequireImageGeneration(JsonObject work,int freeAttempts)
    {
        var status=Doc.Str(work,"status");
        if(status!="image_ready" && status!="image_pending" &&
           !(status=="failed" && Doc.Int(work,"selected",-1)<0))
            throw new InvalidOperationException("当前作品不能重新生成形象");
        if(status=="image_pending" && Doc.Int(work,"image_attempts")>0)
            throw new InvalidOperationException("形象正在生成，请勿重复提交");
        if(Doc.Int(work,"image_attempts")>=freeAttempts)
            throw new InvalidOperationException("免费生成次数已用完");
    }

    public static void RequireImageSelection(JsonObject work,int index)
    {
        if(Doc.Str(work,"status")!="image_ready" || index!=0 ||
           Doc.Array(work,"candidates").Count<1)
            throw new ArgumentException("无法选择此图片");
    }
}

public static class OrderRules
{
    public static bool ShippingValid(JsonObject? o)
    {
        var name=Doc.Str(o,"name").Trim();
        var phone=Doc.Str(o,"phone").Trim();
        var address=Doc.Str(o,"address").Trim();
        return name.EnumerateRunes().Count() is >= 2 and <= 30 && phone.Length is >= 6 and <= 20 &&
               address.EnumerateRunes().Count() is >= 8 and <= 300;
    }

    public static void RequireCancelable(JsonObject order)
    {
        if(Doc.Str(order,"status")!="awaiting_payment")
            throw new InvalidOperationException("只有待付款订单可取消");
        if(Doc.Str(order,"payment_started_at")!="")
            throw new InvalidOperationException("该订单已发起微信支付，请等待支付状态确认后联系客服处理取消");
    }
}
