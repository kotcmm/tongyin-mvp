using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Tongyin.Application.Checkout;
using Tongyin.Domain.Checkout;

namespace Tongyin.Infrastructure.Checkout;

/// <summary>使用与原 Go 后端一致的 ty_orders/ty_addresses JSON 表及 ty_store_lock 事务锁，支持 PostgreSQL 与 MySQL。</summary>
public sealed class SqlCheckoutRepository(Func<DbConnection> connectionFactory) : ICheckoutRepository
{
    /// <summary>读取唯一默认地址，没有时回退到本人最近一笔历史订单的收货快照。</summary>
    public async Task<ShippingAddressSnapshot?> GetShippingAsync(string owner, CancellationToken cancellationToken = default)
    {
        await using var db = connectionFactory();
        await db.OpenAsync(cancellationToken);
        var payload = await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_addresses WHERE id = @Owner AND owner_id = @Owner",
            new { Owner = owner }, cancellationToken: cancellationToken));
        if (payload is not null)
        {
            return DecodeAddress(payload);
        }

        var orders = await db.QueryAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_orders WHERE owner_id = @Owner ORDER BY created_at DESC, id DESC",
            new { Owner = owner }, cancellationToken: cancellationToken));
        foreach (var value in orders)
        {
            var order = DecodeOrder(value);
            if (!string.IsNullOrWhiteSpace(order.Name)
                && !string.IsNullOrWhiteSpace(order.Phone)
                && !string.IsNullOrWhiteSpace(order.Address))
            {
                return new ShippingAddressSnapshot
                {
                    CreatedAt = order.CreatedAt,
                    Name = order.Name,
                    Phone = order.Phone,
                    Address = order.Address
                };
            }
        }

        return null;
    }

    /// <summary>原子替换本人默认收货地址，保持单用户仅一条地址记录。</summary>
    public async Task<ShippingAddressSnapshot> SaveShippingAsync(string owner, ShippingDetails details, CancellationToken cancellationToken = default)
    {
        await using var db = connectionFactory();
        await db.OpenAsync(cancellationToken);
        await using var tx = await db.BeginTransactionAsync(cancellationToken);
        await LockAsync(db, tx, cancellationToken);
        var result = await UpsertAddressAsync(db, tx, owner, details, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>原子验证本人模型已就绪及无其他有效订单，创建待付款订单并维护最新收货地址。</summary>
    public async Task<OrderSnapshot> CreateOrderAsync(string owner, string workId, ShippingDetails details, int priceFen, CancellationToken cancellationToken = default)
    {
        await using var db = connectionFactory();
        await db.OpenAsync(cancellationToken);
        await using var tx = await db.BeginTransactionAsync(cancellationToken);
        await LockAsync(db, tx, cancellationToken);

        var workPayload = await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_works WHERE id = @Id AND owner_id = @Owner",
            new { Id = workId, Owner = owner }, transaction: tx, cancellationToken: cancellationToken));
        var modelReady = false;
        if (workPayload is not null)
        {
            using var document = JsonDocument.Parse(workPayload);
            var root = document.RootElement;
            modelReady = root.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String && status.GetString() == "model_ready"
                && root.TryGetProperty("model", out var model)
                && model.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(model.GetString());
        }

        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        var order = Order.Create(id, owner, workId, modelReady, details, priceFen, DateTimeOffset.Now);

        var existing = await db.QueryAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_orders WHERE owner_id = @Owner",
            new { Owner = owner }, transaction: tx, cancellationToken: cancellationToken));
        foreach (var value in existing)
        {
            var previous = DecodeOrder(value);
            if (previous.WorkId == workId && previous.Status != "canceled")
            {
                throw new InvalidOperationException("该作品已有订单");
            }
        }

        var createdAt = FormatTime(order.CreatedAt);
        var payload = JsonSerializer.Serialize(new
        {
            id = order.Id,
            owner = order.Owner,
            work_id = order.WorkId,
            created_at = createdAt,
            name = details.Name,
            phone = details.Phone,
            address = details.Address,
            price_fen = order.PriceFen,
            status = order.Status,
            history = new[] { new { at = createdAt, actor = "customer", action = "created" } }
        });
        await db.ExecuteAsync(new CommandDefinition(
            "INSERT INTO ty_orders (id, owner_id, created_at, payload) VALUES (@Id, @Owner, @CreatedAt, @Payload)",
            new { Id = order.Id, Owner = owner, CreatedAt = createdAt, Payload = payload },
            transaction: tx, cancellationToken: cancellationToken));
        await UpsertAddressAsync(db, tx, owner, details, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return DecodeOrder(payload);
    }

    /// <summary>只返回当前微信用户的订单，按时间和订单编号倒序。</summary>
    public async Task<IReadOnlyList<OrderSnapshot>> ListOrdersAsync(string owner, CancellationToken cancellationToken = default)
    {
        await using var db = connectionFactory();
        await db.OpenAsync(cancellationToken);
        var payloads = await db.QueryAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_orders WHERE owner_id = @Owner ORDER BY created_at DESC, id DESC",
            new { Owner = owner }, cancellationToken: cancellationToken));
        return payloads.Select(DecodeOrder).ToArray();
    }

    /// <summary>只能通过用户 OpenID 与订单编号联合查询本人订单。</summary>
    public async Task<OrderSnapshot?> GetOrderAsync(string owner, string id, CancellationToken cancellationToken = default)
    {
        await using var db = connectionFactory();
        await db.OpenAsync(cancellationToken);
        var payload = await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_orders WHERE id = @Id AND owner_id = @Owner",
            new { Id = id, Owner = owner }, cancellationToken: cancellationToken));
        return payload is null ? null : DecodeOrder(payload);
    }

    /// <summary>查询本人作品最后一张未取消的订单，供小程序避免重复下单。</summary>
    public async Task<OrderSnapshot?> GetWorkOrderAsync(string owner, string workId, CancellationToken cancellationToken = default)
    {
        await using var db = connectionFactory();
        await db.OpenAsync(cancellationToken);
        var payloads = await db.QueryAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_orders WHERE owner_id = @Owner ORDER BY created_at DESC, id DESC",
            new { Owner = owner }, cancellationToken: cancellationToken));
        foreach (var payload in payloads)
        {
            var order = DecodeOrder(payload);
            if (order.WorkId == workId && order.Status != "canceled")
            {
                return order;
            }
        }

        return null;
    }

    /// <summary>只允许修改本人待付款订单的收货快照，不覆盖价格、付款状态或默认收货地址。</summary>
    public async Task<OrderSnapshot?> UpdateOrderShippingAsync(string owner, string id, ShippingDetails details, CancellationToken cancellationToken = default)
    {
        await using var db = connectionFactory();
        await db.OpenAsync(cancellationToken);
        await using var tx = await db.BeginTransactionAsync(cancellationToken);
        await LockAsync(db, tx, cancellationToken);
        var payload = await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_orders WHERE id = @Id AND owner_id = @Owner",
            new { Id = id, Owner = owner }, transaction: tx, cancellationToken: cancellationToken));
        if (payload is null)
        {
            return null;
        }

        var current = DecodeOrder(payload);
        if (current.Status != "awaiting_payment")
        {
            throw new InvalidOperationException("只有待付款订单可以修改收货地址");
        }

        var data = JsonNode.Parse(payload) as JsonObject
            ?? throw new JsonException("订单不是合法的 JSON 对象");
        data["name"] = details.Name;
        data["phone"] = details.Phone;
        data["address"] = details.Address;
        var updated = data.ToJsonString();
        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_orders SET payload = @Payload WHERE id = @Id AND owner_id = @Owner",
            new { Payload = updated, Id = id, Owner = owner },
            transaction: tx, cancellationToken: cancellationToken));
        await tx.CommitAsync(cancellationToken);
        return DecodeOrder(updated);
    }

    /// <summary>仅待付款且支付未启动可取消，追加订单历史并保留原金额与收货快照。</summary>
    public async Task<OrderSnapshot?> CancelOrderAsync(string owner, string id, string reason, CancellationToken cancellationToken = default)
    {
        await using var db = connectionFactory();
        await db.OpenAsync(cancellationToken);
        await using var tx = await db.BeginTransactionAsync(cancellationToken);
        await LockAsync(db, tx, cancellationToken);
        var payload = await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_orders WHERE id = @Id AND owner_id = @Owner",
            new { Id = id, Owner = owner }, transaction: tx, cancellationToken: cancellationToken));
        if (payload is null)
        {
            return null;
        }

        var current = DecodeOrder(payload);
        if (!Order.CanCancel(current.Status, current.PaymentStartedAt))
        {
            throw new InvalidOperationException("只有待付款订单可取消");
        }

        var data = JsonNode.Parse(payload) as JsonObject
            ?? throw new JsonException("订单不是合法的 JSON 对象");
        data["status"] = "canceled";
        data["cancel_reason"] = reason;
        var history = data["history"] as JsonArray ?? new JsonArray();
        var eventNode = new JsonObject
        {
            ["at"] = FormatTime(DateTimeOffset.Now),
            ["actor"] = "customer",
            ["action"] = "canceled",
            ["note"] = reason
        };
        history.Add(eventNode);
        data["history"] = history;
        var updated = data.ToJsonString();
        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_orders SET payload = @Payload WHERE id = @Id AND owner_id = @Owner",
            new { Payload = updated, Id = id, Owner = owner },
            transaction: tx, cancellationToken: cancellationToken));
        await tx.CommitAsync(cancellationToken);
        return DecodeOrder(updated);
    }

    private static async Task<ShippingAddressSnapshot> UpsertAddressAsync(DbConnection db, DbTransaction tx, string owner, ShippingDetails details, CancellationToken cancellationToken)
    {
        var createdAt = FormatTime(DateTimeOffset.Now);
        var payload = JsonSerializer.Serialize(new
        {
            owner,
            created_at = createdAt,
            name = details.Name,
            phone = details.Phone,
            address = details.Address
        });
        var rows = await db.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_addresses SET payload = @Payload, created_at = @CreatedAt WHERE id = @Owner AND owner_id = @Owner",
            new { Payload = payload, CreatedAt = createdAt, Owner = owner },
            transaction: tx, cancellationToken: cancellationToken));
        if (rows == 0)
        {
            await db.ExecuteAsync(new CommandDefinition(
                "INSERT INTO ty_addresses (id, owner_id, created_at, payload) VALUES (@Owner, @Owner, @CreatedAt, @Payload)",
                new { Owner = owner, CreatedAt = createdAt, Payload = payload },
                transaction: tx, cancellationToken: cancellationToken));
        }

        return DecodeAddress(payload);
    }

    private static async Task LockAsync(DbConnection db, DbTransaction tx, CancellationToken cancellationToken)
    {
        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_store_lock SET id = id WHERE id = 1",
            transaction: tx, cancellationToken: cancellationToken));
        await db.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT id FROM ty_store_lock WHERE id = 1",
            transaction: tx, cancellationToken: cancellationToken));
    }

    private static OrderSnapshot DecodeOrder(string payload)
        => JsonSerializer.Deserialize<OrderSnapshot>(payload)
            ?? throw new JsonException("订单不是合法的 JSON 对象");

    private static ShippingAddressSnapshot DecodeAddress(string payload)
        => JsonSerializer.Deserialize<ShippingAddressSnapshot>(payload)
            ?? throw new JsonException("收货地址不是合法的 JSON 对象");

    private static string FormatTime(DateTimeOffset time)
        => time.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
}
