using System.Text;
namespace Tongyin.Domain.Orders;

public enum OrderStatus { AwaitingPayment, Paid, Printing, Shipped, Completed, Canceled, Unknown }

public sealed record ShippingAddress
{
    public string Name { get; }
    public string Phone { get; }
    public string Address { get; }
    public ShippingAddress(string name,string phone,string address)
    {
        Name=name.Trim(); Phone=phone.Trim(); Address=address.Trim();
        if(Name.EnumerateRunes().Count() is <2 or >30 ||
           Phone.Length is <6 or >20 ||
           Address.EnumerateRunes().Count() is <8 or >300)
            throw new ArgumentException("请填写完整收货信息");
    }
}

public sealed record OrderEvent(string At,string Actor,string Action,string Note="");
public sealed record OrderState(string Id,string Owner,string WorkId,string CreatedAt,
    ShippingAddress Recipient,int PriceFen,OrderStatus Status,
    string PaymentStartedAt,string CancelReason,IReadOnlyList<OrderEvent> History);

public sealed class Order
{
    private readonly List<OrderEvent> history;
    private Order(OrderState state)
    {
        if(string.IsNullOrWhiteSpace(state.Id) || string.IsNullOrWhiteSpace(state.Owner))
            throw new ArgumentException("订单标识或用户无效");
        Id=state.Id;Owner=state.Owner;WorkId=state.WorkId;CreatedAt=state.CreatedAt;
        Recipient=state.Recipient;PriceFen=state.PriceFen;Status=state.Status;
        PaymentStartedAt=state.PaymentStartedAt;CancelReason=state.CancelReason;
        history=state.History.ToList();
    }

    public string Id { get; }
    public string Owner { get; }
    public string WorkId { get; }
    public string CreatedAt { get; }
    public ShippingAddress Recipient { get; private set; }
    public int PriceFen { get; }
    public OrderStatus Status { get; private set; }
    public string PaymentStartedAt { get; }
    public string CancelReason { get; private set; }

    public static Order Place(string id,string owner,string workId,string createdAt,ShippingAddress address,int priceFen)
    {
        if(priceFen<=0)throw new ArgumentOutOfRangeException(nameof(priceFen));
        return new(new OrderState(id,owner,workId,createdAt,address,priceFen,
            OrderStatus.AwaitingPayment,"","",new[]{new OrderEvent(createdAt,"customer","created")}));
    }

    public static Order Restore(OrderState state) => new(state);
    public OrderState Snapshot() => new(Id,Owner,WorkId,CreatedAt,Recipient,PriceFen,Status,
        PaymentStartedAt,CancelReason,history.ToArray());

    // Invariant is checked inside the atomic repository transaction.
    public static void EnsurePlaceable(bool readyWork,bool hasActiveOrder)
    {
        if(!readyWork)throw new InvalidOperationException("模型尚未准备好");
        if(hasActiveOrder)throw new InvalidOperationException("该作品已有订单");
    }

    public void Cancel(string reason,string at)
    {
        if(Status!=OrderStatus.AwaitingPayment)
            throw new InvalidOperationException("只有待付款订单可取消");
        if(PaymentStartedAt!="")
            throw new InvalidOperationException("该订单已发起微信支付，请等待支付状态确认后联系客服处理取消");
        if(reason.EnumerateRunes().Count()>200)
            throw new ArgumentException("取消原因太长");
        Status=OrderStatus.Canceled;CancelReason=reason;
        history.Add(new OrderEvent(at,"customer","canceled",reason));
    }

    public void UpdateAddress(ShippingAddress address)
    {
        if(Status!=OrderStatus.AwaitingPayment)
            throw new InvalidOperationException("只有待付款订单可以修改收货地址");
        Recipient=address;
    }
}
