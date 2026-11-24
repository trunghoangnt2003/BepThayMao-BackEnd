using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Bepthaymao.Domain.Events;
using Bepthaymao.Domain.Orders;
using Bepthaymao.Infrastructure.Messaging;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Bepthaymao.Integration.Tests.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace Bepthaymao.Integration.Tests.Messaging;

/// <summary>
/// Outbox, publisher confirm, inbox, retry và DLQ trên PostgreSQL và RabbitMQ thật (EVT-01, T-03-06/07).
/// Không có broker giả: mỗi lớp test có container RabbitMQ riêng, các test trong lớp chạy tuần tự và dọn hàng đợi trước khi chạy.
/// </summary>
public sealed class OutboxDeliveryTests(InfrastructureFixture infrastructure) : IClassFixture<InfrastructureFixture>, IAsyncLifetime
{
    private const string _meterName = "Bepthaymao.Messaging";
    private static readonly string[] _allQueues = ["inapp.q", "notifier.q", "realtime.q", "webhook-out.q", "report.q"];
    private PaymentWorld _world = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _world = await PaymentWorld.CreateAsync(infrastructure);
        await using IConnection connection = await OpenAsync();
        await using (IChannel channel = await connection.CreateChannelAsync(cancellationToken: Token))
            await OutboxDispatcher.DeclareAsync(channel, Token);
        foreach (string queue in _allQueues)
        {
            await PurgeAsync(connection, queue);
            await PurgeAsync(connection, queue + ".dlq");
            for (int retry = 1; retry <= 5; retry++) await PurgeAsync(connection, queue + ".retry." + retry);
        }
    }

    /// <summary>Hàng đợi chưa tồn tại thì bỏ qua; lỗi NOT_FOUND đóng channel nên mỗi lần dùng channel riêng.</summary>
    private static async Task PurgeAsync(IConnection connection, string queue)
    {
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: Token);
        try { _ = await channel.QueuePurgeAsync(queue, Token); }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException exception) when (exception.ShutdownReason?.ReplyCode == 404) { }
    }

    /// <summary>Số tin sẵn sàng trong hàng; hàng chưa khai báo coi như 0 để assertion báo thiếu hàng đợi thay vì ném lỗi giao thức.</summary>
    private static async Task<uint> DepthAsync(IConnection connection, string queue)
    {
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: Token);
        try { return (await channel.QueueDeclarePassiveAsync(queue, Token)).MessageCount; }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException exception) when (exception.ShutdownReason?.ReplyCode == 404) { return 0; }
    }

    public async ValueTask DisposeAsync()
    {
        await _world.DisposeAsync();
    }

    private async Task<IConnection> OpenAsync() =>
        await new ConnectionFactory { Uri = new(infrastructure.RabbitConnectionString) }.CreateConnectionAsync(Token);

    private async Task<Guid> SeedAsync(string type, object data, DateTimeOffset? at = null)
    {
        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Guid id = Guid.CreateVersion7();
        db.OutboxMessages.Add(new() { Id = id, Type = type, OccurredAt = at ?? _world.Clock.GetUtcNow(), Payload = JsonSerializer.Serialize(data) });
        await db.SaveChangesAsync(Token);
        return id;
    }

    /// <summary>Một vòng phát outbox; tin không giao được (không có route) không được làm vòng phát ném lỗi hay chặn tin khác.</summary>
    private async Task<int> DispatchAsync()
    {
        using IServiceScope scope = _world.OpenScope();
        int published = 0;
        Exception? thrown = await Record.ExceptionAsync(async () => published = await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchAsync(Token));
        Assert.Null(thrown);
        return published;
    }

    /// <summary>Tiêu thụ hết inapp.q bằng cùng pipeline production (<see cref="OutboxDispatcher.ConsumeOneAsync"/>).</summary>
    private async Task DrainInAppAsync(IChannel channel)
    {
        using IServiceScope scope = _world.OpenScope();
        OutboxDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
        Exception? thrown = await Record.ExceptionAsync(async () =>
        {
            for (int guard = 0; guard < 50 && await dispatcher.ConsumeOneAsync(channel, Token); guard++) { }
        });
        Assert.Null(thrown);
    }

    private async Task<OutboxMessage> ReadOutboxAsync(Guid id)
    {
        using IServiceScope scope = _world.OpenScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.AsNoTracking().SingleAsync(value => value.Id == id, Token);
    }

    private async Task<int> CountAsync<T>(Func<AppDbContext, IQueryable<T>> query)
    {
        using IServiceScope scope = _world.OpenScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>()).CountAsync(Token);
    }

    private async Task ExecuteAsync(string sql)
    {
        using IServiceScope scope = _world.OpenScope();
        _ = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(sql, Token);
    }

    private const string _failTrigger = """
        CREATE OR REPLACE FUNCTION fail_alert() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'injected failure'; END $$ LANGUAGE plpgsql;
        CREATE TRIGGER fail_alert BEFORE INSERT ON operational_alerts FOR EACH ROW WHEN (NEW.type = 'payment.late') EXECUTE FUNCTION fail_alert();
        """;

    private static byte[] LateEvent(Guid eventId, Guid orderId, Guid branchId, int version = 1) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        eventId,
        type = "payment.late",
        occurredAt = DateTimeOffset.UtcNow,
        version,
        data = new { orderId, branchId }
    });

    private static BasicProperties Props(Guid id, int? retry = null) => new()
    {
        Persistent = true,
        MessageId = id.ToString(),
        Headers = retry is null ? null : new Dictionary<string, object?> { ["btm-retry"] = retry.Value }
    };

    [Fact]
    public async Task OrderCommitSurvivesBrokerDownAndPublishesAfterRecovery()
    {
        Order order = await _world.PlaceAsync();
        using HttpResponseMessage paid = await _world.SendAsync(_world.Transfer(order));
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        int pending = await CountAsync(db => db.OutboxMessages.Where(value => value.PublishedAt == null));
        Assert.True(pending >= 2);
        string databaseConnection;
        using (IServiceScope scope = _world.OpenScope()) databaseConnection = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString()!;
        using ApiFactory brokerDown = new(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = databaseConnection,
            ["ConnectionStrings:RabbitMq"] = "amqp://guest:guest@127.0.0.1:1/",
            ["SePay:WebhookKey"] = PaymentWorld.Key
        }, _world.Clock);
        using (IServiceScope down = brokerDown.Services.CreateScope())
            _ = await Assert.ThrowsAnyAsync<Exception>(() => down.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchAsync(Token));
        Assert.Equal(pending, await CountAsync(db => db.OutboxMessages.Where(value => value.PublishedAt == null)));
        _ = await DispatchAsync();
        Assert.Equal(0, await CountAsync(db => db.OutboxMessages.Where(value => value.PublishedAt == null)));
    }

    [Fact]
    public async Task UnroutablePublishIsNotMarkedSent()
    {
        Guid unroutable = await SeedAsync("unrouted.test", new { value = 1 }, _world.Clock.GetUtcNow().AddSeconds(-5));
        Guid routable = await SeedAsync("order.created", new { orderId = Guid.NewGuid(), branchId = _world.BranchA });
        Assert.Equal(1, await DispatchAsync());
        OutboxMessage row = await ReadOutboxAsync(unroutable);
        Assert.Null(row.PublishedAt);
        Assert.Equal(1, row.Attempts);
        // Tin không có route đứng trước không được chặn tin hợp lệ phía sau trong cùng vòng.
        Assert.NotNull((await ReadOutboxAsync(routable)).PublishedAt);
    }

    [Fact]
    public async Task EventsWaitInQueuesOfFutureConsumersAndAreMarkedSent()
    {
        Guid created = await SeedAsync("order.created", new { orderId = Guid.NewGuid(), branchId = _world.BranchA });
        Guid export = await SeedAsync("report.export_requested", new { jobId = Guid.NewGuid() });
        Assert.Equal(2, await DispatchAsync());
        Assert.NotNull((await ReadOutboxAsync(created)).PublishedAt);
        Assert.NotNull((await ReadOutboxAsync(export)).PublishedAt);
        await using IConnection connection = await OpenAsync();
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: Token);
        foreach (string queue in new[] { "notifier.q", "realtime.q", "webhook-out.q" })
            Assert.Equal(1u, await DepthAsync(connection, queue));
        Assert.Equal(1u, await DepthAsync(connection, "report.q"));
    }

    [Fact]
    public async Task CrashAfterConfirmBeforeMarkRedeliversWithoutDoubleSideEffect()
    {
        Order order = await _world.PlaceAsync();
        Guid id = await SeedAsync("payment.late", new { orderId = order.Id, branchId = _world.BranchA });
        _ = await DispatchAsync();
        // Mô phỏng crash sau khi broker confirm nhưng trước khi đánh dấu: dòng outbox vẫn chưa gửi nên bị giao lại cùng eventId.
        using (IServiceScope scope = _world.OpenScope())
            _ = await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.Where(value => value.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.PublishedAt, (DateTimeOffset?)null).SetProperty(value => value.ClaimedUntil, (DateTimeOffset?)null), Token);
        Assert.Equal(1, await DispatchAsync());
        await using IConnection connection = await OpenAsync();
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: Token);
        await DrainInAppAsync(channel);
        Assert.Equal(1, await CountAsync(db => db.InboxMessages.Where(value => value.Consumer == "inapp.q" && value.EventId == id)));
        Assert.Equal(1, await CountAsync(db => db.OperationalAlerts.Where(value => value.Type == "payment.late" && value.OrderId == order.Id)));
    }

    [Fact]
    public async Task InboxKeyIsPerConsumerAndEventId()
    {
        Guid eventId = Guid.NewGuid();
        using (IServiceScope scope = _world.OpenScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.InboxMessages.AddRange(new InboxMessage { Consumer = "inapp.q", EventId = eventId, ProcessedAt = _world.Clock.GetUtcNow() },
                new InboxMessage { Consumer = "notifier.q", EventId = eventId, ProcessedAt = _world.Clock.GetUtcNow() });
            await db.SaveChangesAsync(Token);
        }
        using IServiceScope duplicate = _world.OpenScope();
        AppDbContext other = duplicate.ServiceProvider.GetRequiredService<AppDbContext>();
        other.InboxMessages.Add(new() { Consumer = "inapp.q", EventId = eventId, ProcessedAt = _world.Clock.GetUtcNow() });
        _ = await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task PoisonMessageGoesStraightToDeadLetterWithDurableAlert()
    {
        Guid id = Guid.NewGuid();
        await using IConnection connection = await OpenAsync();
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: Token);
        await channel.BasicPublishAsync("", "inapp.q", false, Props(id), "{không phải json"u8.ToArray(), Token);
        await DrainInAppAsync(channel);
        Assert.Equal(1u, await DepthAsync(connection, "inapp.q.dlq"));
        Assert.Equal(0u, await DepthAsync(connection, "inapp.q.retry.1"));
        Assert.Equal(1, await CountAsync(db => db.OperationalAlerts.Where(value => value.Type == "messaging.dead_letter")));
    }

    [Fact]
    public async Task UnsupportedSchemaVersionGoesStraightToDeadLetter()
    {
        Guid id = Guid.NewGuid();
        await using IConnection connection = await OpenAsync();
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: Token);
        await channel.BasicPublishAsync("", "inapp.q", false, Props(id), LateEvent(id, Guid.NewGuid(), _world.BranchA, version: 2), Token);
        await DrainInAppAsync(channel);
        Assert.Equal(1u, await DepthAsync(connection, "inapp.q.dlq"));
        Assert.Equal(0, await CountAsync(db => db.InboxMessages.Where(value => value.Consumer == "inapp.q")));
    }

    [Fact]
    public async Task TransientConsumerFailureRollsBackAndRetriesWithDelay()
    {
        Order order = await _world.PlaceAsync();
        Guid id = Guid.NewGuid();
        await ExecuteAsync(_failTrigger);
        await using IConnection connection = await OpenAsync();
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: Token);
        await channel.BasicPublishAsync("", "inapp.q", false, Props(id), LateEvent(id, order.Id, _world.BranchA), Token);
        using IServiceScope scope = _world.OpenScope();
        Exception? thrown = await Record.ExceptionAsync(() => scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().ConsumeOneAsync(channel, Token));
        Assert.Null(thrown);
        Assert.Equal(0, await CountAsync(db => db.InboxMessages.Where(value => value.EventId == id)));
        Assert.Equal(1u, await DepthAsync(connection, "inapp.q.retry.1"));
        Assert.Equal(0u, await DepthAsync(connection, "inapp.q"));
        // Hết trễ (2 giây) tin quay lại hàng chính; lỗi hết thì xử lý đúng một lần.
        await ExecuteAsync("DROP TRIGGER fail_alert ON operational_alerts");
        for (int wait = 0; wait < 100 && await DepthAsync(connection, "inapp.q") == 0; wait++) await Task.Delay(100, Token);
        await DrainInAppAsync(channel);
        Assert.Equal(1, await CountAsync(db => db.InboxMessages.Where(value => value.EventId == id)));
        Assert.Equal(1, await CountAsync(db => db.OperationalAlerts.Where(value => value.Type == "payment.late" && value.OrderId == order.Id)));
    }

    [Fact]
    public async Task RetriesStopAfterFiveAttemptsAndDeadLetterAlertsOnce()
    {
        Order order = await _world.PlaceAsync();
        Guid id = Guid.NewGuid();
        await ExecuteAsync(_failTrigger);
        await using IConnection connection = await OpenAsync();
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: Token);
        await channel.BasicPublishAsync("", "inapp.q", false, Props(id, retry: 4), LateEvent(id, order.Id, _world.BranchA), Token);
        await DrainInAppAsync(channel);
        Assert.Equal(1u, await DepthAsync(connection, "inapp.q.retry.5"));
        Assert.Equal(0u, await DepthAsync(connection, "inapp.q.dlq"));
        await channel.BasicPublishAsync("", "inapp.q", false, Props(id, retry: 5), LateEvent(id, order.Id, _world.BranchA), Token);
        await DrainInAppAsync(channel);
        Assert.Equal(1u, await DepthAsync(connection, "inapp.q.dlq"));
        await channel.BasicPublishAsync("", "inapp.q", false, Props(id, retry: 5), LateEvent(id, order.Id, _world.BranchA), Token);
        await DrainInAppAsync(channel);
        Assert.Equal(1, await CountAsync(db => db.OperationalAlerts.Where(value => value.Type == "messaging.dead_letter")));
    }

    [Fact]
    public async Task StuckOutboxMessageRaisesDurableAlertOnce()
    {
        Guid id = await SeedAsync("unrouted.test", new { value = 1 });
        for (int attempt = 0; attempt < 7; attempt++)
        {
            _ = await DispatchAsync();
            _world.Clock.Advance(TimeSpan.FromSeconds(31));
        }
        Assert.True((await ReadOutboxAsync(id)).Attempts >= 5);
        Assert.Null((await ReadOutboxAsync(id)).PublishedAt);
        Assert.Equal(1, await CountAsync(db => db.OperationalAlerts.Where(value => value.Type == "messaging.outbox_stuck")));
    }

    [Fact]
    public async Task TwoWorkersNeverPublishTheSameEventTwice()
    {
        for (int index = 0; index < 12; index++) _ = await SeedAsync("order.created", new { orderId = Guid.NewGuid(), branchId = _world.BranchA, index });
        _ = await Task.WhenAll(Task.Run(DispatchAsync, Token), Task.Run(DispatchAsync, Token));
        _ = await DispatchAsync();
        await using IConnection connection = await OpenAsync();
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: Token);
        Assert.Equal(12u, await DepthAsync(connection, "notifier.q"));
        Assert.Equal(0, await CountAsync(db => db.OutboxMessages.Where(value => value.PublishedAt == null)));
    }

    [Fact]
    public async Task PayloadsCarryNoPersonalData()
    {
        Order order = await _world.PlaceAsync();
        using HttpResponseMessage paid = await _world.SendAsync(_world.Transfer(order, amount: 50000));
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        using IServiceScope scope = _world.OpenScope();
        List<OutboxMessage> messages = await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.AsNoTracking().ToListAsync(Token);
        Assert.NotEmpty(messages);
        string[] forbidden = ["name", "phone", "address", "email", "note", "authorization", "apikey"];
        foreach (OutboxMessage message in messages)
        {
            Assert.DoesNotContain(PaymentWorld.Phone, message.Payload, StringComparison.Ordinal);
            using JsonDocument document = JsonDocument.Parse(message.Payload);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
                Assert.DoesNotContain(forbidden, word => property.Name.Contains(word, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task ConsumerNeverUpdatesDishStats()
    {
        Order order = await _world.PlaceAsync();
        Guid id = Guid.NewGuid();
        await ExecuteAsync($"INSERT INTO dish_stats (dish_id, branch_id, order_count) VALUES ('{_world.DishId}', '{_world.BranchA}', 7)");
        await using IConnection connection = await OpenAsync();
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: Token);
        await channel.BasicPublishAsync("", "inapp.q", false, Props(id), LateEvent(id, order.Id, _world.BranchA), Token);
        await DrainInAppAsync(channel);
        Assert.Equal(1, await CountAsync(db => db.InboxMessages.Where(value => value.EventId == id)));
        using IServiceScope scope = _world.OpenScope();
        long total = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
            .SqlQuery<long>($"SELECT COALESCE(SUM(order_count), 0)::bigint AS \"Value\" FROM dish_stats").SingleAsync(Token);
        Assert.Equal(7, total);
    }

    [Fact]
    public async Task MessagingCountersReportPublishUnroutableAndDeadLetter()
    {
        Dictionary<string, long> totals = [];
        using MeterListener listener = new()
        {
            InstrumentPublished = (instrument, source) =>
            {
                if (instrument.Meter.Name == _meterName) source.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            lock (totals) totals[instrument.Name] = totals.GetValueOrDefault(instrument.Name) + value;
        });
        listener.Start();
        _ = await SeedAsync("order.created", new { orderId = Guid.NewGuid(), branchId = _world.BranchA });
        _ = await SeedAsync("unrouted.test", new { value = 1 });
        _ = await DispatchAsync();
        await using IConnection connection = await OpenAsync();
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: Token);
        await channel.BasicPublishAsync("", "inapp.q", false, Props(Guid.NewGuid()), "{không phải json"u8.ToArray(), Token);
        await DrainInAppAsync(channel);
        lock (totals)
        {
            Assert.True(totals.GetValueOrDefault("bepthaymao.outbox.published") >= 1);
            Assert.True(totals.GetValueOrDefault("bepthaymao.outbox.unroutable") >= 1);
            Assert.True(totals.GetValueOrDefault("bepthaymao.consumer.dead_lettered") >= 1);
        }
    }
}
