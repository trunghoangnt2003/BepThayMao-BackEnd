using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Bepthaymao.Application.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Bepthaymao.Infrastructure.Persistence.Orders;

internal sealed class OrderAccessGrantService(AppDbContext db, TimeProvider clock, IConfiguration configuration) : IOrderAccessGrants
{
    public IssuedOrderAccess Issue(Guid orderId)
    {
        string token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset expiresAt = now.AddHours(configuration.GetValue("Orders:AccessCookie:Hours", 24));
        db.OrderAccessGrants.Add(new OrderAccessGrant
        {
            Id = Guid.CreateVersion7(),
            OrderId = orderId,
            TokenHash = Hash(token),
            CreatedAt = now,
            ExpiresAt = expiresAt,
        });
        return new(token, expiresAt);
    }

    public async Task<bool> AllowsAsync(Guid orderId, IReadOnlyList<string> rawTokens, CancellationToken cancellationToken)
    {
        if (rawTokens.Count == 0) return false;
        string[] hashes = [.. rawTokens.Take(5).Select(Hash)];
        DateTimeOffset now = clock.GetUtcNow();
        return await db.OrderAccessGrants.AsNoTracking()
            .AnyAsync(grant => grant.OrderId == orderId && hashes.Contains(grant.TokenHash) && grant.ExpiresAt > now, cancellationToken);
    }

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
