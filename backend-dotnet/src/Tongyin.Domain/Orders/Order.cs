using System.Text;
namespace Tongyin.Domain.Orders;

/// <summary>
/// 订单聚合根，维护下单、取消及收货信息的业务规则。
/// </summary>
public sealed class Order
{
    private readonly List<OrderEvent> _history;
    private Order(OrderState state)
    {
        if(string.IsNullOrWhiteSpace(state.Id) || string.IsNullOrWhiteSpace(state.Owner))
            throw new ArgumentException("订单标识或用户无效");
        Id=state.Id;Owner=state.Owner;WorkId=state.WorkId;CreatedAt=state.CreatedAt;
        Recipient=state.Recipient;PriceFen=state.PriceFen;Status=state.Status;
        PaymentStartedAt=state.PaymentStartedAt;CancelReason=state.CancelReason;
        _history=state.History.ToList();
    }

    /// <summary>
    /// 唯一标识。
    /// </summary>
    public string Id { get; }
    /// <summary>
    /// 所属用户标识。
    /// </summary>
    public string Owner { get; }
    /// <summary>
    /// 关联作品标识。
    /// </summary>
    public string WorkId { get; }
    /// <summary>
    /// 创建时间。
    /// </summary>
    public string CreatedAt { get; }
    /// <summary>
    /// 收货信息。
    /// </summary>
    public ShippingAddress Recipient { get; private set; }
    /// <summary>
    /// 订单价格，单位为分。
    /// </summary>
    public int PriceFen { get; }
    /// <summary>
    /// 当前业务状态。
    /// </summary>
    public OrderStatus Status { get; private set; }
    /// <summary>
    /// 支付请求开始时间。
    /// </summary>
    public string PaymentStartedAt { get; }
    /// <summary>
    /// 取消原因。
    /// </summary>
    public string CancelReason { get; private set; }

    /// <summary>
    /// 创建待支付订单聚合。
    /// </summary>
    public static Order Place(string id,string owner,string workId,string createdAt,ShippingAddress address,int priceFen)
    {
        if(priceFen<=0)throw new ArgumentOutOfRangeException(nameof(priceFen));
        return new(new OrderState(id,owner,workId,createdAt,address,priceFen,
            OrderStatus.AwaitingPayment,"","",new[]{new OrderEvent(createdAt,"customer","created")}));
    }

    /// <summary>
    /// 根据已存储状态还原业务对象。
    /// </summary>
    public static Order Restore(OrderState state) => new(state);
    /// <summary>
    /// 取得独立的业务状态快照。
    /// </summary>
    public OrderState Snapshot() => new(Id,Owner,WorkId,CreatedAt,Recipient,PriceFen,Status,
        PaymentStartedAt,CancelReason,_history.ToArray());

    // Invariant is checked inside the atomic repository transaction.
    /// <summary>
    /// 检查作品是否可下单以及是否存在重复订单。
    /// </summary>
    public static void EnsurePlaceable(bool readyWork,bool hasActiveOrder)
    {
        if(!readyWork)throw new InvalidOperationException("模型尚未准备好");
        if(hasActiveOrder)throw new InvalidOperationException("该作品已有订单");
    }

    /// <summary>
    /// 根据订单当前状态执行取消操作。
    /// </summary>
    public void Cancel(string reason,string at)
    {
        if(Status!=OrderStatus.AwaitingPayment)
            throw new InvalidOperationException("只有待付款订单可取消");
        if(PaymentStartedAt!="")
            throw new InvalidOperationException("该订单已发起微信支付，请等待支付状态确认后联系客服处理取消");
        if(reason.EnumerateRunes().Count()>200)
            throw new ArgumentException("取消原因太长");
        Status=OrderStatus.Canceled;CancelReason=reason;
        _history.Add(new OrderEvent(at,"customer","canceled",reason));
    }

    /// <summary>
    /// 校验订单状态并修改收货信息。
    /// </summary>
    public void UpdateAddress(ShippingAddress address)
    {
        if(Status!=OrderStatus.AwaitingPayment)
            throw new InvalidOperationException("只有待付款订单可以修改收货地址");
        Recipient=address;
    }
}
