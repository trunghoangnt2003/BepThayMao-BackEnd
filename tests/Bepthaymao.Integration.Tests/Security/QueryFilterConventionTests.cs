using Bepthaymao.Domain.Branches;
using Bepthaymao.Domain.Common;
using Bepthaymao.Infrastructure.Identity;
using Bepthaymao.Infrastructure.Persistence;
using Bepthaymao.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Bepthaymao.Integration.Tests.Security;

public sealed class QueryFilterConventionTests
{
    [Fact]
    public void EveryBranchScopedEntityHasNamedBranchScopeFilter()
    {
        using AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only").UseSnakeCaseNamingConvention().Options, new TestDataScope(true, null, null));
        IModel model = db.GetService<IDesignTimeModel>().Model;
        IEntityType[] scoped = [.. model.GetEntityTypes().Where(entityType =>
            entityType.ClrType != typeof(AppUser) &&
            (entityType.ClrType == typeof(Branch) || typeof(IBranchScoped).IsAssignableFrom(entityType.ClrType) ||
             entityType.ClrType.GetProperty("BranchId") is not null))];
        Assert.Contains(scoped, entityType => entityType.ClrType == typeof(Branch));
        foreach (IEntityType entityType in scoped)
            Assert.True(entityType.FindDeclaredQueryFilter("BranchScope") is not null,
                $"{entityType.ClrType.Name} thiếu query filter tên BranchScope.");
    }
}
