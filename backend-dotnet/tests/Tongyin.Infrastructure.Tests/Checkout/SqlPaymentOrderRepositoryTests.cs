using System.Data.Common;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Tongyin.Application.Payments;
using Tongyin.Infrastructure.Checkout;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Checkout;

/// <summary>微信支付状态在 Go 兼容 ty_orders 表中的原子提交和幂等验证。</summary>
public sealed class SqlPaymentOrderRepositoryTests
{
    /// <summary>付款前先保存 payment_started_at，且不能修改订单金额或状态。</summary>
    [Fact]
    public async Task Reserve_UnpaidOrder_SetsPaymentStartedAtOnlyOnce()
    {
        using var db = new PaymentDatabase();
        db.Seed("awaiting_payment");
        var repo = new SqlPaymentOrderRepository(db.Connect);
        var first = await repo.ReserveAsync("owner-1", "order-1");
        var second = await repo.ReserveAsync("owner-1", "order-1");

        Assert.NotNull(first);
        Assert.Equal("awaiting_payment", first.Status);
        Assert.NotEmpty(first.PaymentStartedAt!);
        Assert.Equal(first.PaymentStartedAt, second!.PaymentStartedAt);
        Assert.Equal(12900, second.PriceFen);
    }

    /// <summary>支付成功会写状态与流水，同一通知重复送达绝不能重复追加历史。</summary>
    [Fact]
    public async Task Confirm_SuccessThenDuplicate_IsIdempotent()
    {
        using var db = new PaymentDatabase();
        db.Seed("awaiting_payment");
        var repo = new SqlPaymentOrderRepository(db.Connect);
        var first = await repo.ConfirmSuccessAsync("order-1", "owner-1", 12900, "tx-001");
        var second = await repo.ConfirmSuccessAsync("order-1", "owner-1", 12900, "tx-001");

        Assert.Equal(PaymentConfirmationResult.Paid, first);
        Assert.Equal(PaymentConfirmationResult.Duplicate, second);
        using var json = JsonDocument.Parse(db.Read());
        var order = json.RootElement;
        Assert.Equal("paid", order.GetProperty("status").GetString());
        Assert.Equal("tx-001", order.GetProperty("wechat_transaction_id").GetString());
        Assert.Equal(2, order.GetProperty("history").GetArrayLength());
        Assert.Equal("保留", order.GetProperty("legacy_field").GetString());
    }

    /// <summary>错误金额、付款人或不一致的微信流水不能更改已有订单。</summary>
    [Theory]
    [InlineData("wrong-user", 12900, "tx-001")]
    [InlineData("owner-1", 1, "tx-001")]
    [InlineData("owner-1", 12900, "")]
    public async Task Confirm_UntrustedTransaction_RejectsWithoutMutation(string payer, int total, string transactionId)
    {
        using var db = new PaymentDatabase();
        db.Seed("awaiting_payment");
        var original = db.Read();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SqlPaymentOrderRepository(db.Connect)
            .ConfirmSuccessAsync("order-1", payer, total, transactionId));

        Assert.Equal(original, db.Read());
    }

    /// <summary>取消后发生的真实入账必须标记为已付款并申请人工退款，而不是丢弃资金流水。</summary>
    [Fact]
    public async Task Confirm_AfterCancellation_MarksRefundReview()
    {
        using var db = new PaymentDatabase();
        db.Seed("canceled");
        var result = await new SqlPaymentOrderRepository(db.Connect)
            .ConfirmSuccessAsync("order-1", "owner-1", 12900, "tx-002");

        Assert.Equal(PaymentConfirmationResult.PaidAfterCancellation, result);
        using var json = JsonDocument.Parse(db.Read());
        Assert.Equal("paid", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("requested", json.RootElement.GetProperty("refund_status").GetString());
    }

    /// <summary>其他用户无法发起支付，已付款订单不能再次预下单。</summary>
    [Fact]
    public async Task Reserve_ForeignOrAlreadyPaid_IsRejected()
    {
        using var db = new PaymentDatabase();
        db.Seed("paid");
        var repo = new SqlPaymentOrderRepository(db.Connect);
        Assert.Null(await repo.ReserveAsync("other", "order-1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ReserveAsync("owner-1", "order-1"));
    }

    private sealed class PaymentDatabase : IDisposable
    {
        private readonly string _file = Path.Combine(Path.GetTempPath(), "tongyin-pay-" + Guid.NewGuid().ToString("N") + ".db");
        public PaymentDatabase()
        {
            using var connection = Connect();
            connection.Open();
            connection.Execute("CREATE TABLE ty_orders(id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL); CREATE TABLE ty_store_lock (id INTEGER PRIMARY KEY); INSERT INTO ty_store_lock(id) VALUES (1)");
        }

        public DbConnection Connect() => new SqliteConnection($"Data Source={_file};Pooling=False");
        public void Seed(string status)
        {
            using var db = Connect();
            db.Open();
            var payload = JsonSerializer.Serialize(new
            {
                id = "order-1", owner = "owner-1", work_id = "work-1",
                created_at = "2026-10-17T08:00:00+08:00", price_fen = 12900, status,
                name = "张三", phone = "13800138000", address = "广州市海珠区新港东路100号",
                history = new[] { new { at = "2026-10-17T08:00:00+08:00", actor = "customer", action = "created" } },
                legacy_field = "保留"
            });
            db.Execute("INSERT INTO ty_orders(id, owner_id, created_at, payload) VALUES ('order-1','owner-1','2026-10-17',@Payload)", new { Payload = payload });
        }
        public string Read()
        {
            using var db = Connect();
            db.Open();
            return db.QuerySingle<string>("SELECT payload FROM ty_orders WHERE id='order-1'");
        }
        public void Dispose()
        {
            if (File.Exists(_file)) File.Delete(_file);
        }
    }
}
