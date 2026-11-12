using Bepthaymao.Domain.Menu;
using Bepthaymao.Domain.Shop;
using Bepthaymao.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bepthaymao.Infrastructure.Seeding;

/// <summary>
/// Thực đơn, banner và đoạn giới thiệu mẫu cho môi trường Development; chỉ tạo khi Owner chưa có món (idempotent).
/// Tên, giá và nội dung là dữ liệu mẫu, chủ quán thay bằng dữ liệu thật.
/// </summary>
public sealed class MenuSampleSeeder(AppDbContext db, TimeProvider clock)
{
    private static readonly (string Name, string Description, long Price, DishCategory Category, bool Featured)[] _dishes =
    [
        ("Xôi xéo", "Nếp vàng, đậu xanh, hành phi giòn.", 25000, DishCategory.XoiMan, false),
        ("Xôi gà xé", "Gà luộc xé, nước mỡ hành, dưa góp.", 40000, DishCategory.XoiMan, true),
        ("Xôi lạp xưởng", "Lạp xưởng áp chảo, ruốc, hành phi.", 35000, DishCategory.XoiMan, false),
        ("Xôi pate chả", "Pate gan, chả lụa, dưa góp.", 35000, DishCategory.XoiMan, false),
        ("Xôi gấc", "Nếp gấc đỏ, dừa nạo, chút đường.", 30000, DishCategory.XoiNgot, true),
        ("Xôi lá dứa", "Nếp lá dứa thơm, đậu xanh.", 25000, DishCategory.Chay, false),
        ("Xôi đỗ đen", "Nếp cái hoa vàng, muối vừng.", 20000, DishCategory.Chay, false),
    ];

    public async Task<int> SeedAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        if (await db.Dishes.AnyAsync(dish => dish.OwnerId == ownerId, cancellationToken)) return 0;
        DateTimeOffset now = clock.GetUtcNow();
        List<Dish> dishes = [.. _dishes.Select((dish, index) => Dish.Create(Guid.CreateVersion7(), ownerId,
            new DishDetails(dish.Name, dish.Description, dish.Price, null, dish.Category, dish.Featured, index), now))];
        db.Dishes.AddRange(dishes);
        Dish featured = dishes.First(dish => dish.Name == "Xôi gấc");
        db.ShopBanners.AddRange(
            ShopBanner.Create(Guid.CreateVersion7(), ownerId, new("Bếp Thầy Mão", "Xôi nóng giao tận nơi từ 3 cơ sở.", null, null, 0, true), now),
            ShopBanner.Create(Guid.CreateVersion7(), ownerId, new("Xôi gấc", "Món quán giới thiệu tuần này.", null, featured.Id, 1, true), now),
            ShopBanner.Create(Guid.CreateVersion7(), ownerId, new("Đặt xôi trong 3 bước", "Chọn món, chọn cơ sở, quét QR.", null, null, 2, true), now));
        if (!await db.ShopAbouts.AnyAsync(about => about.OwnerId == ownerId, cancellationToken))
            db.ShopAbouts.Add(ShopAbout.Create(ownerId, "Đoạn giới thiệu mẫu: quán xôi gia đình, nấu mỗi sáng tại từng cơ sở. Chủ quán thay bằng câu chuyện thật.", now));
        Guid? secondBranch = await db.Branches.IgnoreQueryFilters([AppDbContext.BranchScope])
            .Where(branch => branch.OwnerId == ownerId).OrderBy(branch => branch.Name).Skip(1).Select(branch => (Guid?)branch.Id).FirstOrDefaultAsync(cancellationToken);
        if (secondBranch is Guid branchId)
            db.BranchDishes.Add(BranchDish.Create(branchId, dishes.First(dish => dish.Name == "Xôi pate chả").Id, false, now));
        await db.SaveChangesAsync(cancellationToken);
        return dishes.Count;
    }
}
