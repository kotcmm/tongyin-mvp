using System.Data.Common;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Tongyin.Domain.Checkout;
using Tongyin.Infrastructure.Checkout;
using Xunit;

namespace Tongyin.Infrastructure.Tests.Checkout;

/// <summary>直接验证 Go 兼容订单和地址表的事务边界，避免绕过 API 时重复下单与支付状态误判。</summary>
public sealed class SqlCheckoutRepositoryTests
{
    private static ShippingDetails Shipping => ShippingDetails.Create("张三", "13800138000", "广州市海珠区新港东路100号");

    /// <summary>用户保存地址不应被其他用户读取，且不能覆盖他人唯一地址。</summary>
    [Fact]
    public async Task SaveShipping_SeparatesUsers()
    {
        using var store = new TestStore();
        var repo = new SqlCheckoutRepository(store.Connect);
        await repo.SaveShippingAsync("openid-1", Shipping);

        Assert.Null(await repo.GetShippingAsync("openid-2"));
        Assert.Equal("张三", (await repo.GetShippingAsync("openid-1"))!.Name);
        Assert.Equal(1, store.Count("ty_addresses"));
    }

    /// <summary>历史 Go 订单仍然可以在无默认地址记录时恢复收货信息。</summary>
    [Fact]
    public async Task GetShipping_NoAddress_UsesLatestHistoricalOrder()
    {
        using var store = new TestStore();
        store.SeedOrder("old-1", "openid-1", "work-1", "paid",
            paymentStartedAt: "", createdAt: "2026-10-16T09:00:00+08:00");

        var address = await new SqlCheckoutRepository(store.Connect).GetShippingAsync("openid-1");

        Assert.NotNull(address);
        Assert.Equal("张三", address.Name);
        Assert.Equal("13800138000", address.Phone);
        Assert.Equal("广州市海珠区新港东路100号", address.Address);
        Assert.Equal(0, store.Count("ty_addresses"));
    }

    /// <summary>同一作品重复创建订单时只保留第一笔；跨账号不能借用他人的模型。</summary>
    [Fact]
    public async Task CreateOrder_DuplicateOrOtherOwner_IsRejectedWithoutExtraRows()
    {
        using var store = new TestStore();
        store.SeedWork("work-1", "openid-1", "model_ready");
        var repo = new SqlCheckoutRepository(store.Connect);
        var first = await repo.CreateOrderAsync("openid-1", "work-1", Shipping, 12900);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.CreateOrderAsync("openid-1", "work-1", Shipping, 12900));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.CreateOrderAsync("openid-2", "work-1", Shipping, 12900));

        Assert.Equal(1, store.Count("ty_orders"));
        Assert.Equal("awaiting_payment", first.Status);
        Assert.Empty(await repo.ListOrdersAsync("openid-2"));
    }

    /// <summary>发起微信支付后的订单不能取消，即使状态暂时还是 awaiting_payment。</summary>
    [Fact]
    public async Task Cancel_PaymentStarted_RejectsWithoutMutation()
    {
        using var store = new TestStore();
        store.SeedOrder("order-1", "openid-1", "work-1", "awaiting_payment",
            paymentStartedAt: "2026-10-17T10:00:00+08:00");
        var before = store.ReadOrder("order-1");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlCheckoutRepository(store.Connect).CancelOrderAsync("openid-1", "order-1", "不需要了"));

        Assert.Equal(before, store.ReadOrder("order-1"));
    }

    /// <summary>已付款订单不能再被用户改收货信息，防止后台发货时使用变化的快照。</summary>
    [Fact]
    public async Task UpdateShipping_PaidOrder_RejectsWithoutMutation()
    {
        using var store = new TestStore();
        store.SeedOrder("order-1", "openid-1", "work-1", "paid", "");
        var before = store.ReadOrder("order-1");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlCheckoutRepository(store.Connect).UpdateOrderShippingAsync("openid-1", "order-1",
                ShippingDetails.Create("李四", "13800138000", "广州市天河区体育西路100号")));

        Assert.Equal(before, store.ReadOrder("order-1"));
    }

    /// <summary>取消后的旧订单不阻止用户重新下单，且保留历史记录供后台核对。</summary>
    [Fact]
    public async Task CreateOrder_AfterCanceledOrder_IsAllowed()
    {
        using var store = new TestStore();
        store.SeedWork("work-1", "openid-1", "model_ready");
        store.SeedOrder("old-1", "openid-1", "work-1", "canceled", "");
        var result = await new SqlCheckoutRepository(store.Connect)
            .CreateOrderAsync("openid-1", "work-1", Shipping, 12900);

        Assert.Equal("awaiting_payment", result.Status);
        Assert.Equal(2, store.Count("ty_orders"));
    }

    private sealed class TestStore : IDisposable
    {
        private readonly string _file = Path.Combine(Path.GetTempPath(), "tongyin-checkout-sql-" + Guid.NewGuid().ToString("N") + ".db");

        public TestStore()
        {
            using var db = Connect();
            db.Open();
            db.Execute("""
                CREATE TABLE ty_works(id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_orders(id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_addresses(id TEXT PRIMARY KEY, owner_id TEXT NOT NULL, created_at TEXT NOT NULL, payload TEXT NOT NULL);
                CREATE TABLE ty_store_lock(id INTEGER PRIMARY KEY);
                INSERT INTO ty_store_lock(id) VALUES (1);
                """);
        }

        public DbConnection Connect() => new SqliteConnection($"Data Source={_file};Pooling=False");

        public void SeedWork(string id, string owner, string status)
        {
            using var db = Connect();
            db.Open();
            db.Execute("INSERT INTO ty_works(id,owner_id,created_at,payload) VALUES (@Id,@Owner,'2026-10-17',@Payload)",
                new { Id = id, Owner = owner, Payload = JsonSerializer.Serialize(new
                {
                    id, owner, status, created_at = "2026-10-17T08:00:00+08:00",
                    original = "drawing.png", model = "model.glb", candidates = new[] { "cartoon.png" }, selected = 0
                }) });
        }

        public void SeedOrder(string id, string owner, string workId, string status, string paymentStartedAt, string createdAt = "2026-10-17T08:00:00+08:00")
        {
            using var db = Connect();
            db.Open();
            db.Execute("INSERT INTO ty_orders(id,owner_id,created_at,payload) VALUES (@Id,@Owner,@CreatedAt,@Payload)",
                new { Id = id, Owner = owner, CreatedAt = createdAt, Payload = JsonSerializer.Serialize(new
                {
                    id, owner, work_id = workId, created_at = createdAt, status,
                    name = "张三", phone = "13800138000", address = "广州市海珠区新港东路100号",
                    price_fen = 12900, payment_started_at = paymentStartedAt,
                    history = new[] { new { at = createdAt, actor = "customer", action = "created" } }
                }) });
        }

        public string ReadOrder(string id)
        {
            using var db = Connect();
            db.Open();
            return db.QuerySingle<string>("SELECT payload FROM ty_orders WHERE id=@Id", new { Id = id });
        }

        public int Count(string table)
        {
            using var db = Connect();
            db.Open();
            return db.ExecuteScalar<int>("SELECT COUNT(*) FROM " + (table == "ty_orders" ? "ty_orders" : "ty_addresses"));
        }

        public void Dispose()
        {
            if (File.Exists(_file)) File.Delete(_file);
        }
    }
}
