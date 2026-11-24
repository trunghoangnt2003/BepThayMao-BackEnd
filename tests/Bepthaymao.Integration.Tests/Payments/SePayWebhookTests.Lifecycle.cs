using System.Net;
using System.Text.Json;
using Bepthaymao.Api.Security;
using Bepthaymao.Domain.Orders;
using Bepthaymao.Infrastructure.Messaging;
using Bepthaymao.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace Bepthaymao.Integration.Tests.Payments;

// Thuộc 03-04 (outbox/Rabbit): cần OutboxDispatcher. Hai test hết hạn từng ở đây đã chuyển sang OrderExpiryTests/PaymentExpiryRaceTests (03-03).
public sealed partial class SePayWebhookTests
{
    [Fact]
    public async Task BrokerPublishAndInboxRedeliveryAreSafe()
    {
        Order order = await PlaceAsync(16);
        using HttpResponseMessage paid = await SendAsync(Transfer(order));
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        using IServiceScope scope = _factory.Services.CreateSystemScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        OutboxDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
        Assert.True(await dispatcher.DispatchAsync(Token) >= 2);
        Assert.Equal(0, await db.OutboxMessages.CountAsync(value => value.PublishedAt == null, Token));
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            eventId = Guid.NewGuid(),
            type = "payment.late",
            occurredAt = _clock.GetUtcNow(),
            version = 1,
            data = new { orderId = order.Id, branchId = _branch }
        });
        await dispatcher.ProcessAsync(body, Token); await dispatcher.ProcessAsync(body, Token);
        using IServiceScope check = _factory.Services.CreateSystemScope();
        AppDbContext verify = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await verify.InboxMessages.CountAsync(value => value.Consumer == OutboxDispatcher.Queue, Token));
        ConnectionFactory factory = new() { Uri = new(infrastructure.RabbitConnectionString) };
        await using IConnection connection = await factory.CreateConnectionAsync(Token);
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: Token);
        await channel.BasicPublishAsync("", OutboxDispatcher.Queue, false,
            new BasicProperties { MessageId = Guid.NewGuid().ToString(), Headers = new Dictionary<string, object?> { ["btm-retry"] = 5 } }, "{}"u8.ToArray(), Token);
        Assert.True(await dispatcher.ConsumeOneAsync(channel, Token));
        Assert.NotNull(await channel.BasicGetAsync(OutboxDispatcher.Queue + ".dlq", true, Token));
        Assert.True(await verify.OperationalAlerts.AnyAsync(value => value.Type == "messaging.dead_letter", Token));
    }
}
