using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Tongyin.Application.Checkout;
using Tongyin.Application.Payments;

namespace Tongyin.Infrastructure.Checkout;

/// <summary>在 Go 兼容的 ty_orders JSON 表中原子登记支付申请和微信验签成功的订单状态。</summary>
public sealed class SqlPaymentOrderRepository(Func<DbConnection> connectionFactory) : IPaymentOrderRepository
{
    /// <summary>先持久化 payment_started_at；支付可能已提交时禁止客户取消订单。</summary>
    public async Task<OrderSnapshot?> ReserveAsync(string owner, string orderId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireLockAsync(connection, transaction, cancellationToken);

        var payload = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT payload FROM ty_orders WHERE id = @Id AND owner_id = @Owner",
            new { Id = orderId, Owner = owner }, transaction: transaction, cancellationToken: cancellationToken));
        if (payload is null)
        {
            return null;
        }

        var order = Decode(payload);
        if (order.Status != "awaiting_payment")
        {
            throw new InvalidOperationException("订单状态不允许支付");
        }

        if (string.IsNullOrWhiteSpace(order.PaymentStartedAt))
        {
            var json = Parse(payload);
            json["payment_started_at"] = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
            payload = json.ToJsonString();
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE ty_orders SET payload = @Payload WHERE id = @Id AND owner_id = @Owner",
                new { Payload = payload, Id = orderId, Owner = owner },
                transaction: transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return Decode(payload);
    }

    /// <summary>核实 OpenID、原订单金额、微信交易号并保证同一到账通知最多变更一次订单。</summary>
    public async Task<PaymentConfirmationResult> ConfirmSuccessAsync(string orderId, string payerOpenId, int totalFen, string transactionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(payerOpenId)
            || string.IsNullOrWhiteSpace(transactionId) || totalFen <= 0)
        {
            throw new InvalidOperationException("微信交易关键字段缺失或无效");
        }

        await using var connection = connectionFactory();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireLockAsync(connection, transaction, cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<(string Owner, string Payload)>(new CommandDefinition(
            "SELECT owner_id AS Owner, payload AS Payload FROM ty_orders WHERE id = @Id",
            new { Id = orderId }, transaction: transaction, cancellationToken: cancellationToken));
        if (row.Payload is null)
        {
            throw new InvalidOperationException("没有找到对应商户订单");
        }

        var order = Decode(row.Payload);
        if (row.Owner != payerOpenId || order.PriceFen != totalFen)
        {
            throw new InvalidOperationException("微信支付付款人或金额与订单不符");
        }

        var doc = Parse(row.Payload);
        var previousTransaction = doc["wechat_transaction_id"]?.GetValue<string>();
        if (order.Status is "paid" or "printing" or "shipped" or "completed")
        {
            if (!string.IsNullOrEmpty(previousTransaction) && previousTransaction != transactionId)
            {
                throw new InvalidOperationException("相同订单出现了不一致的微信交易流水");
            }
            return PaymentConfirmationResult.Duplicate;
        }

        if (order.Status is not ("awaiting_payment" or "canceled"))
        {
            throw new InvalidOperationException("订单当前状态不允许结算");
        }

        var canceled = order.Status == "canceled";
        doc["status"] = "paid";
        doc["wechat_transaction_id"] = transactionId;
        if (canceled)
        {
            doc["refund_status"] = "requested";
        }

        var history = doc["history"] as JsonArray ?? new JsonArray();
        history.Add(new JsonObject
        {
            ["at"] = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
            ["actor"] = "wechatpay",
            ["action"] = canceled ? "paid_after_cancel" : "paid",
            ["note"] = canceled ? "需要核实并退款" : "微信支付通知已验签"
        });
        doc["history"] = history;

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_orders SET payload = @Payload WHERE id = @Id",
            new { Payload = doc.ToJsonString(), Id = orderId },
            transaction: transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return canceled ? PaymentConfirmationResult.PaidAfterCancellation : PaymentConfirmationResult.Paid;
    }

    private static OrderSnapshot Decode(string payload)
        => JsonSerializer.Deserialize<OrderSnapshot>(payload) ?? throw new JsonException("订单记录无效");

    private static JsonObject Parse(string payload)
        => JsonNode.Parse(payload) as JsonObject ?? throw new JsonException("订单记录无效");

    private static async Task AcquireLockAsync(DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ty_store_lock SET id = id WHERE id = 1",
            transaction: transaction, cancellationToken: cancellationToken));
        await connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT id FROM ty_store_lock WHERE id = 1",
            transaction: transaction, cancellationToken: cancellationToken));
    }
}
