using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Tongyin.Application.Checkout;
using Tongyin.Domain.Checkout;

namespace Tongyin.Infrastructure.Checkout;

/// <summary>基于 Go 现有 ty_orders 和 ty_audit 表的 PostgreSQL/MySQL 兼容履约存储。</summary>
public sealed class SqlOrderFulfillmentRepository(Func<DbConnection> connectionFactory) : IOrderFulfillmentRepository
{
    /// <summary>按时间倒序查询全部订单，仅由管理员入口调用。</summary>
    public async Task<IReadOnlyList<OrderSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = connectionFactory();
        await db.OpenAsync(cancellationToken);
        var values = await db.QueryAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_orders ORDER BY created_at DESC, id DESC", cancellationToken: cancellationToken));
        return values.Select(Decode).ToArray();
    }

    /// <summary>在全局写锁内核实可信支付状态并持久化制作中与审计记录。</summary>
    public async Task<OrderSnapshot?> StartPrintingAsync(string id, string actor, CancellationToken cancellationToken = default)
    {
        await using var db = connectionFactory();
        await db.OpenAsync(cancellationToken);
        await using var tx = await db.BeginTransactionAsync(cancellationToken);
        await LockAsync(db, tx, cancellationToken);
        var payload = await FindAsync(db, tx, id, cancellationToken);
        if (payload is null) return null;
        var current = Decode(payload);
        OrderFulfillmentRules.RequireStartPrinting(current.Status, current.RefundStatus);
        var data = Parse(payload);
        data["status"] = "printing";
        var timestamp = TimeNow();
        AddHistory(data, timestamp, actor, "status.printing", null);
        await SaveAndAuditAsync(db, tx, id, actor, timestamp, "order.status", "printing", data, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return Decode(data.ToJsonString());
    }

    /// <summary>在同一事务中保存物流信息、状态和审计；重复发货不会覆盖旧运单。</summary>
    public async Task<OrderSnapshot?> ShipAsync(string id, string actor, ShipmentDetails details, CancellationToken cancellationToken = default)
    {
        await using var db = connectionFactory();
        await db.OpenAsync(cancellationToken);
        await using var tx = await db.BeginTransactionAsync(cancellationToken);
        await LockAsync(db, tx, cancellationToken);
        var payload = await FindAsync(db, tx, id, cancellationToken);
        if (payload is null) return null;
        var current = Decode(payload);
        OrderFulfillmentRules.RequireShipment(current.Status, current.RefundStatus);
        var data = Parse(payload);
        var timestamp = TimeNow();
        data["carrier"] = details.Carrier;
        data["carrier_code"] = details.CarrierCode;
        data["tracking"] = details.Tracking;
        data["status"] = "shipped";
        data["shipped_at"] = timestamp;
        data["tracking_state"] = "unavailable";
        data["tracking_events"] = new JsonArray();
        data["tracking_checked_at"] = "";
        AddHistory(data, timestamp, actor, "shipped", details.Carrier + " " + details.Tracking);
        await SaveAndAuditAsync(db, tx, id, actor, timestamp, "order.shipped", details.Carrier, data, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return Decode(data.ToJsonString());
    }

    /// <summary>同时核验订单编号和 OpenID，未发货时不得展示虚构物流。</summary>
    public async Task<OrderTrackingSnapshot?> GetTrackingAsync(string owner, string id, CancellationToken cancellationToken = default)
    {
        await using var db = connectionFactory();
        await db.OpenAsync(cancellationToken);
        var payload = await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_orders WHERE id = @Id AND owner_id = @Owner",
            new { Id = id, Owner = owner }, cancellationToken: cancellationToken));
        if (payload is null) return null;
        var order = Decode(payload);
        OrderFulfillmentRules.RequireTrackingAvailable(order.Status);
        var events = order.TrackingEvents ?? [];
        return new OrderTrackingSnapshot
        {
            Company = order.Carrier ?? "",
            CompanyCode = order.CarrierCode ?? "",
            TrackingNo = order.Tracking ?? "",
            TrackingState = order.TrackingState ?? "",
            UpdatedAt = order.TrackingCheckedAt ?? "",
            Events = events,
            Source = events.Count == 0 ? "unavailable"
                : events.Any(item => item.Source == "kuaidi100") ? "kuaidi100" : "manual"
        };
    }

    private static async Task<string?> FindAsync(DbConnection db, DbTransaction tx, string id, CancellationToken cancellationToken)
        => await db.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_orders WHERE id = @Id",
            new { Id = id }, transaction: tx, cancellationToken: cancellationToken));

    private static async Task LockAsync(DbConnection db, DbTransaction tx, CancellationToken cancellationToken)
    {
        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_store_lock SET id = id WHERE id = 1", transaction: tx, cancellationToken: cancellationToken));
        await db.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT id FROM ty_store_lock WHERE id = 1", transaction: tx, cancellationToken: cancellationToken));
    }

    private static void AddHistory(JsonObject data, string timestamp, string actor, string action, string? note)
    {
        var history = data["history"] as JsonArray ?? new JsonArray();
        var eventData = new JsonObject
        {
            ["at"] = timestamp,
            ["actor"] = actor,
            ["action"] = action
        };
        if (note is not null) eventData["note"] = note;
        history.Add(eventData);
        data["history"] = history;
    }

    private static async Task SaveAndAuditAsync(DbConnection db, DbTransaction tx, string id, string actor, string timestamp, string action, string detail, JsonObject data, CancellationToken cancellationToken)
    {
        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_orders SET payload = @Payload WHERE id = @Id",
            new { Payload = data.ToJsonString(), Id = id }, transaction: tx, cancellationToken: cancellationToken));
        var auditId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var auditJson = JsonSerializer.Serialize(new
        {
            id = auditId,
            owner = actor,
            created_at = timestamp,
            action,
            target = id,
            detail
        });
        await db.ExecuteAsync(new CommandDefinition(
            "INSERT INTO ty_audit(id, owner_id, created_at, payload) VALUES (@Id, @Owner, @CreatedAt, @Payload)",
            new { Id = auditId, Owner = actor, CreatedAt = timestamp, Payload = auditJson },
            transaction: tx, cancellationToken: cancellationToken));
    }

    private static OrderSnapshot Decode(string payload)
        => JsonSerializer.Deserialize<OrderSnapshot>(payload) ?? throw new JsonException("订单记录格式错误");

    private static JsonObject Parse(string payload)
        => JsonNode.Parse(payload) as JsonObject ?? throw new JsonException("订单记录格式错误");

    private static string TimeNow()
        => DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
}
