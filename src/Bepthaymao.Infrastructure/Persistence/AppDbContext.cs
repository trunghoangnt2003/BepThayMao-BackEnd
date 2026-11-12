using Bepthaymao.Application.Abstractions.Persistence;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Domain.Owners;
using Bepthaymao.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bepthaymao.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, IDataScope scope) : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IUnitOfWork
{
    // Thành viên của DbContext (không phải hằng số): EF thay bằng chính instance đang truy vấn nên mô hình
    // được cache theo kiểu context vẫn cô lập đúng từng phạm vi (P17).
    private bool IsSystemScope => scope.IsSystem;
    private Guid? CurrentOwnerId => scope.OwnerId;
    private Guid? CurrentBranchId => scope.BranchId;

    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Owner> Owners => Set<Owner>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<AppUser>(user =>
        {
            user.ToTable("users");
            user.HasIndex(value => value.BranchId).IsUnique();
            user.HasOne<Owner>().WithMany().HasForeignKey(value => value.OwnerId).OnDelete(DeleteBehavior.Restrict);
            user.HasOne<Branch>().WithMany().HasForeignKey(value => value.BranchId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<IdentityRole<Guid>>(role =>
        {
            role.ToTable("roles");
            role.HasData(
                new IdentityRole<Guid> { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Name = "Owner", NormalizedName = "OWNER", ConcurrencyStamp = "seed-owner-v1" },
                new IdentityRole<Guid> { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), Name = "BranchManager", NormalizedName = "BRANCHMANAGER", ConcurrencyStamp = "seed-manager-v1" },
                new IdentityRole<Guid> { Id = Guid.Parse("00000000-0000-0000-0000-000000000003"), Name = "Customer", NormalizedName = "CUSTOMER", ConcurrencyStamp = "seed-customer-v1" });
        });
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        builder.Entity<Owner>(owner =>
        {
            owner.ToTable("owners");
            owner.HasKey(value => value.Id);
            owner.Property(value => value.Id).ValueGeneratedNever();
            owner.Property(value => value.Name).HasMaxLength(200);
        });
        builder.Entity<RefreshToken>(token =>
        {
            token.ToTable("refresh_tokens");
            token.HasKey(value => value.Id);
            token.Property(value => value.Id).ValueGeneratedNever();
            token.Property(value => value.TokenHash).HasMaxLength(64);
            token.HasIndex(value => value.TokenHash).IsUnique();
            token.HasIndex(value => value.UserId);
            token.HasOne<AppUser>().WithMany().HasForeignKey(value => value.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Branch>(branch =>
        {
            branch.ToTable("branches", table =>
            {
                table.HasCheckConstraint("ck_branches_open_before_close", "open_time < close_time");
                table.HasCheckConstraint("ck_branches_lat", "lat BETWEEN -90 AND 90");
                table.HasCheckConstraint("ck_branches_lng", "lng BETWEEN -180 AND 180");
                table.HasCheckConstraint("ck_branches_abandoned_threshold", "abandoned_threshold_minutes >= 1");
            });
            branch.HasKey(value => value.Id);
            branch.Property(value => value.Id).ValueGeneratedNever();
            branch.Property(value => value.Name).HasMaxLength(200);
            branch.Property(value => value.Address).HasMaxLength(500);
            branch.Property(value => value.Phone).HasMaxLength(20);
            branch.HasOne<Owner>().WithMany().HasForeignKey(value => value.OwnerId).OnDelete(DeleteBehavior.Restrict);
            branch.Property(value => value.AbandonedThresholdMinutes).HasDefaultValue(10);
            branch.Property(value => value.ZaloRecipientIds).HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]");
            branch.Property(value => value.BankAccountNo).HasMaxLength(50);
            branch.Property(value => value.BankCode).HasMaxLength(20);
            branch.Property(value => value.SepayAccountRef).HasMaxLength(100);
        });
    }
    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) => await SaveChangesAsync(cancellationToken);

    async Task IUnitOfWork.ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await work(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            ChangeTracker.Clear();
            throw;
        }
    }
}
