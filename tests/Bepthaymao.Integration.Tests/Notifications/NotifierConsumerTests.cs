using System.Text.Json;
using Bepthaymao.Domain.Events;
using Bepthaymao.Domain.Orders;
using Bepthaymao.Infrastructure.Notifications;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Bepthaymao.Integration.Tests.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Integration.Tests.Notifications;

[Collection(SharedInfrastructure.Name)]
public sealed class NotifierConsumerTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private PaymentWorld _world = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _world = await PaymentWorld.CreateAsync(infrastructure);
    public async ValueTask DisposeAsync() => await _world.DisposeAsync();

    private sealed class StubSender(Exception? failure = null) : ITelegramSender
    {
        public List<(long ChatId, string Html)> Sent { get; } = [];
        public Task SendHtmlAsync(long chatId, string htmlText, CancellationToken cancellationToken)
        {
            if (failure is not null) throw failure;
            Sent.Add((chatId, htmlText));
            return Task.CompletedTask;
        }
    }

    private static byte[] Envelope(Guid eventId, string type, object data) =>
        JsonSerializer.SerializeToUtf8Bytes(new { eventId, type, occurredAt = DateTimeOffset.UtcNow, version = 1, data }, JsonSerializerOptions.Web);

    private async Task ProcessAsync(ITelegramSender sender, byte[] body)
    {
        using IServiceScope scope = _world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await new NotifierConsumer(db, sender, _world.Clock).ProcessAsync(body, 0, Token);
    }

    private async Task<List<Notification>> NotificationsAsync(Guid orderId)
    {
        using IServiceScope scope = _world.OpenScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Notifications.AsNoTracking()
            .Where(n => n.OrderId == orderId).OrderBy(n => n.CreatedAt).ToListAsync(Token);
    }

    [Fact]
    public async Task PaidEventSendsOneMessageAndDuplicateDeliveryIsIgnored()
    {
        await NotificationWorld.SetChatIdAsync(_world, _world.BranchA, NotificationWorld.ChatId);
        Order order = await NotificationWorld.PlacePaidAsync(_world);
        StubSender sender = new();
        byte[] body = Envelope(Guid.NewGuid(), "order.paid", new { orderId = order.Id });

        await ProcessAsync(sender, body);
        await ProcessAsync(sender, body);

        (long chatId, string html) = Assert.Single(sender.Sent);
        Assert.Equal(NotificationWorld.ChatId, chatId);
        Assert.Contains(order.Code, html, StringComparison.Ordinal);
        Notification saved = Assert.Single(await NotificationsAsync(order.Id));
        Assert.Equal((NotificationType.OrderNew, NotificationStatus.Sent), (saved.Type, saved.Status));
    }

    [Fact]
    public async Task BranchWithoutChatIdIsSkippedWithoutSending()
    {
        Order order = await NotificationWorld.PlacePaidAsync(_world);
        StubSender sender = new();

        await ProcessAsync(sender, Envelope(Guid.NewGuid(), "order.paid", new { orderId = order.Id }));

        Assert.Empty(sender.Sent);
        Assert.Equal(NotificationStatus.Skipped, Assert.Single(await NotificationsAsync(order.Id)).Status);
    }

    [Fact]
    public async Task PermanentTelegramErrorRecordsFailureAndInAppMisconfiguredAlert()
    {
        await NotificationWorld.SetChatIdAsync(_world, _world.BranchA, NotificationWorld.ChatId);
        Order order = await NotificationWorld.PlacePaidAsync(_world);

        await ProcessAsync(new StubSender(new TelegramPermanentException(403, "Telegram API lỗi cấu hình (403).")),
            Envelope(Guid.NewGuid(), "order.paid", new { orderId = order.Id }));

        List<Notification> saved = await NotificationsAsync(order.Id);
        Assert.Contains(saved, n => n is { Channel: NotificationChannel.Telegram, Status: NotificationStatus.Failed });
        Assert.Contains(saved, n => n is { Type: NotificationType.TelegramMisconfigured, Channel: NotificationChannel.InApp });
    }

    [Fact]
    public async Task TransientTelegramErrorPropagatesForRetryAndWritesNothing()
    {
        await NotificationWorld.SetChatIdAsync(_world, _world.BranchA, NotificationWorld.ChatId);
        Order order = await NotificationWorld.PlacePaidAsync(_world);

        await Assert.ThrowsAsync<TelegramTransientException>(() => ProcessAsync(
            new StubSender(new TelegramTransientException("Telegram API tạm lỗi (503).")),
            Envelope(Guid.NewGuid(), "order.paid", new { orderId = order.Id })));

        Assert.Empty(await NotificationsAsync(order.Id));
    }

    [Fact]
    public async Task OnlyCancellationFromPaidOrAcceptedNotifies()
    {
        await NotificationWorld.SetChatIdAsync(_world, _world.BranchA, NotificationWorld.ChatId);
        Order order = await NotificationWorld.PlacePaidAsync(_world);
        StubSender sender = new();

        await ProcessAsync(sender, Envelope(Guid.NewGuid(), "order.status_changed", new { orderId = order.Id, from = "Paid", to = "Accepted" }));
        await ProcessAsync(sender, Envelope(Guid.NewGuid(), "order.status_changed", new { orderId = order.Id, from = "PendingPayment", to = "Cancelled" }));
        Assert.Empty(sender.Sent);

        await ProcessAsync(sender, Envelope(Guid.NewGuid(), "order.status_changed", new { orderId = order.Id, from = "Accepted", to = "Cancelled" }));
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task AbandonedEventSendsAlertWithWaitedMinutes()
    {
        await NotificationWorld.SetChatIdAsync(_world, _world.BranchA, NotificationWorld.ChatId);
        Order order = await NotificationWorld.PlacePaidAsync(_world);
        StubSender sender = new();

        await ProcessAsync(sender, Envelope(Guid.NewGuid(), "order.abandoned", new { orderId = order.Id, waitedMinutes = 12 }));

        Assert.Contains("12", Assert.Single(sender.Sent).Html, StringComparison.Ordinal);
        Notification saved = Assert.Single(await NotificationsAsync(order.Id));
        Assert.Equal((NotificationType.AbandonedAlert, 12), (saved.Type, saved.WaitedMinutes));
    }

    [Fact]
    public async Task MalformedEnvelopeIsRejected()
    {
        await Assert.ThrowsAsync<FormatException>(() => ProcessAsync(new StubSender(),
            Envelope(Guid.Empty, "order.paid", new { orderId = Guid.NewGuid() })));
    }
}
