using System.Reflection;
using Bepthaymao.Application.Abstractions.Persistence;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Domain.Branches;
using Bepthaymao.Domain.Common;
using Bepthaymao.Domain.Menu;
using Bepthaymao.Domain.Orders;
using Bepthaymao.Domain.Shop;
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
    public DbSet<Dish> Dishes => Set<Dish>();
    public DbSet<BranchDish> BranchDishes => Set<BranchDish>();
    public DbSet<ShopBanner> ShopBanners => Set<ShopBanner>();
    public DbSet<ShopAbout> ShopAbouts => Set<ShopAbout>();
    public DbSet<ShopLegal> ShopLegals => Set<ShopLegal>();
    public DbSet<ShopPolicy> ShopPolicies => Set<ShopPolicy>();
    public DbSet<Order> Orders => Set<Order>();
    public const string BranchScope = "BranchScope";

    // Owner là superadmin trên cơ sở của mình (spec FR-003a): thấy bản ghi của mọi cơ sở có Branch.OwnerId trùng claim,
    // không thấy cơ sở của Owner khác. Quản lý chỉ thấy cơ sở trong claim branch_id.
    private void ApplyBranchScope<T>(ModelBuilder builder) where T : class, IBranchScoped =>
        builder.Entity<T>().HasQueryFilter(BranchScope, entity => IsSystemScope
            || (CurrentBranchId != null && entity.BranchId == CurrentBranchId)
            || (CurrentOwnerId != null && Branches.Any(branch => branch.Id == entity.BranchId && branch.OwnerId == CurrentOwnerId)));

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        MethodInfo applyBranchScope = typeof(AppDbContext).GetMethod(nameof(ApplyBranchScope), BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (Type clrType in builder.Model.GetEntityTypes().Select(entityType => entityType.ClrType)
                     .Where(type => typeof(IBranchScoped).IsAssignableFrom(type)).ToList())
            applyBranchScope.MakeGenericMethod(clrType).Invoke(this, [builder]);
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
            branch.HasQueryFilter(BranchScope, value => IsSystemScope
                || (CurrentOwnerId != null && value.OwnerId == CurrentOwnerId)
                || (CurrentBranchId != null && value.Id == CurrentBranchId));
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
        builder.Entity<Dish>(dish =>
        {
            dish.ToTable("dishes", table => table.HasCheckConstraint("ck_dishes_price", "price >= 0"));
            dish.HasKey(value => value.Id);
            dish.Property(value => value.Id).ValueGeneratedNever();
            dish.Property(value => value.Name).HasMaxLength(Dish.NameMaxLength);
            dish.Property(value => value.Description).HasMaxLength(Dish.DescriptionMaxLength);
            dish.Property(value => value.ImageUrl).HasMaxLength(500);
            dish.Property(value => value.Category).HasConversion<string>().HasMaxLength(20);
            dish.HasOne<Owner>().WithMany().HasForeignKey(value => value.OwnerId).OnDelete(DeleteBehavior.Restrict);
            dish.HasIndex(value => new { value.OwnerId, value.Category, value.SortOrder });
        });
        builder.Entity<BranchDish>(state =>
        {
            state.ToTable("branch_dishes");
            state.HasKey(value => new { value.BranchId, value.DishId });
            state.HasOne<Branch>().WithMany().HasForeignKey(value => value.BranchId).OnDelete(DeleteBehavior.Cascade);
            state.HasOne<Dish>().WithMany().HasForeignKey(value => value.DishId).OnDelete(DeleteBehavior.Cascade);
            state.HasIndex(value => new { value.BranchId, value.IsAvailable });
        });
        builder.Entity<ShopBanner>(banner =>
        {
            banner.ToTable("shop_banners");
            banner.HasKey(value => value.Id);
            banner.Property(value => value.Id).ValueGeneratedNever();
            banner.Property(value => value.Title).HasMaxLength(ShopBanner.TitleMaxLength);
            banner.Property(value => value.Body).HasMaxLength(ShopBanner.BodyMaxLength);
            banner.Property(value => value.ImageUrl).HasMaxLength(500);
            banner.HasOne<Owner>().WithMany().HasForeignKey(value => value.OwnerId).OnDelete(DeleteBehavior.Restrict);
            banner.HasOne<Dish>().WithMany().HasForeignKey(value => value.LinkDishId).OnDelete(DeleteBehavior.SetNull);
        });
        builder.Entity<ShopAbout>(about =>
        {
            about.ToTable("shop_abouts");
            about.HasKey(value => value.OwnerId);
            about.Property(value => value.About).HasMaxLength(ShopAbout.MaxLength);
            about.HasOne<Owner>().WithMany().HasForeignKey(value => value.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<ShopLegal>(legal =>
        {
            legal.ToTable("shop_legals");
            legal.HasKey(value => value.OwnerId);
            legal.Property(value => value.BusinessName).HasMaxLength(ShopLegal.NameMaxLength);
            legal.Property(value => value.RegistrationNumber).HasMaxLength(ShopLegal.RegistrationMaxLength);
            legal.Property(value => value.RegistrationIssuedBy).HasMaxLength(ShopLegal.IssuedByMaxLength);
            legal.Property(value => value.Address).HasMaxLength(ShopLegal.AddressMaxLength);
            legal.Property(value => value.Phone).HasMaxLength(ShopLegal.PhoneMaxLength);
            legal.Property(value => value.Email).HasMaxLength(ShopLegal.EmailMaxLength);
            legal.Property(value => value.MoitNoticeUrl).HasMaxLength(ShopLegal.NoticeUrlMaxLength);
            legal.HasOne<Owner>().WithMany().HasForeignKey(value => value.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<ShopPolicy>(policy =>
        {
            policy.ToTable("shop_policies");
            policy.HasKey(value => new { value.OwnerId, value.Kind });
            policy.Property(value => value.Kind).HasConversion<string>().HasMaxLength(20);
            policy.Property(value => value.Body).HasMaxLength(ShopPolicy.BodyMaxLength);
            policy.HasOne<Owner>().WithMany().HasForeignKey(value => value.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Order>(order =>
        {
            order.ToTable("orders", table => table.HasCheckConstraint("ck_orders_items_total", "items_total >= 0"));
            order.HasKey(value => value.Id);
            order.Property(value => value.Id).ValueGeneratedNever();
            order.Property(value => value.Code).HasMaxLength(9);
            order.HasIndex(value => value.Code).IsUnique();
            order.Property(value => value.CustomerName).HasMaxLength(120);
            order.Property(value => value.CustomerPhone).HasMaxLength(15);
            order.Property(value => value.DeliveryAddress).HasMaxLength(300);
            order.Property(value => value.Note).HasMaxLength(500);
            order.Property(value => value.CancelReason).HasMaxLength(50);
            order.Property(value => value.Status).HasConversion<string>().HasMaxLength(20);
            order.Property(value => value.PaymentStatus).HasConversion<string>().HasMaxLength(20);
            order.HasOne<Branch>().WithMany().HasForeignKey(value => value.BranchId).OnDelete(DeleteBehavior.Restrict);
            order.HasIndex(value => new { value.BranchId, value.Status, value.CreatedAt });
            order.HasIndex(value => new { value.Status, value.ExpiresAt });
            order.HasMany(value => value.Items).WithOne().HasForeignKey(item => item.OrderId).OnDelete(DeleteBehavior.Cascade);
            order.Navigation(value => value.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        builder.Entity<OrderItem>(item =>
        {
            item.ToTable("order_items", table => table.HasCheckConstraint("ck_order_items_quantity", "quantity BETWEEN 1 AND 99"));
            item.HasKey(value => value.Id);
            item.Property(value => value.Id).ValueGeneratedNever();
            item.Property(value => value.DishName).HasMaxLength(Dish.NameMaxLength);
            item.HasOne<Dish>().WithMany().HasForeignKey(value => value.DishId).OnDelete(DeleteBehavior.Restrict);
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
