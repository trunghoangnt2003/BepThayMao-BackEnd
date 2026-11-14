using Bepthaymao.Application.Shop;
using Bepthaymao.Domain.Shop;
using Microsoft.EntityFrameworkCore;

namespace Bepthaymao.Infrastructure.Persistence.Menu;

internal sealed class LegalStore(AppDbContext context) : ILegalStore
{
    public Task<ShopLegal?> FindLegalAsync(Guid? ownerId, CancellationToken cancellationToken) =>
        context.ShopLegals.Where(legal => ownerId == null || legal.OwnerId == ownerId)
            .OrderByDescending(legal => legal.UpdatedAt).FirstOrDefaultAsync(cancellationToken);

    public void AddLegal(ShopLegal legal) => context.ShopLegals.Add(legal);

    public async Task<IReadOnlyList<ShopPolicy>> ListPoliciesAsync(Guid? ownerId, CancellationToken cancellationToken) =>
        await context.ShopPolicies.Where(policy => ownerId == null || policy.OwnerId == ownerId).ToListAsync(cancellationToken);

    public void AddPolicy(ShopPolicy policy) => context.ShopPolicies.Add(policy);
}
