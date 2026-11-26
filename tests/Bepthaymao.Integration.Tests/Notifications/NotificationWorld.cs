using Bepthaymao.Domain.Orders;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bepthaymao.Integration.Tests.Notifications;

internal static class NotificationWorld
{
    public const long ChatId = -1001234567890;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static async Task SetChatIdAsync(PaymentWorld world, Guid branchId, long? chatId)
    {
        using IServiceScope scope = world.OpenScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
            .ExecuteSqlAsync($"UPDATE branches SET telegram_chat_id = {chatId} WHERE id = {branchId}", Token);
    }

    public static async Task<Order> PlacePaidAsync(PaymentWorld world, int paidMinutesAgo = 0)
    {
        Order order = await world.PlaceAsync();
        using IServiceScope scope = world.OpenScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Order tracked = await db.Orders.SingleAsync(value => value.Id == order.Id, Token);
        DateTimeOffset paidAt = world.Clock.GetUtcNow().AddMinutes(-paidMinutesAgo);
        Assert.True(tracked.ReceivePayment(tracked.ItemsTotal, paidAt, paidAt));
        await db.SaveChangesAsync(Token);
        return tracked;
    }
}
