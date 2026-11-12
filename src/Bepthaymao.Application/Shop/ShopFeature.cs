using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Persistence;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Domain.Shop;
using FluentValidation;

namespace Bepthaymao.Application.Shop;

public interface IShopStore
{
    /// <summary><paramref name="ownerId"/> null là mọi Owner (luồng công khai).</summary>
    Task<IReadOnlyList<ShopBanner>> ListBannersAsync(Guid? ownerId, CancellationToken cancellationToken);
    Task<ShopBanner?> FindBannerAsync(Guid bannerId, CancellationToken cancellationToken);
    void AddBanner(ShopBanner banner);
    void RemoveBanner(ShopBanner banner);
    /// <summary><paramref name="ownerId"/> null lấy đoạn giới thiệu mới nhất của bất kỳ Owner (luồng công khai).</summary>
    Task<ShopAbout?> FindAboutAsync(Guid? ownerId, CancellationToken cancellationToken);
    void AddAbout(ShopAbout about);
}

public sealed record BannerDto(Guid Id, string Title, string? Body, string? ImageUrl, Guid? LinkDishId, int SortOrder, bool IsActive);

public sealed record ShopContentDto(IReadOnlyList<BannerDto> Banners, string? About);

public sealed record BannerInputData(string Title, string? Body, string? ImageUrl, Guid? LinkDishId, int SortOrder, bool IsActive)
{
    public BannerDetails ToDetails() => new(Title, Body, ImageUrl, LinkDishId, SortOrder, IsActive);
}

public sealed record GetShopContentQuery : IQuery<ShopContentDto>;

public sealed record ListOwnerBannersQuery : IQuery<IReadOnlyList<BannerDto>>;

public sealed record CreateOwnerBannerCommand(BannerInputData Banner) : ICommand<BannerDto>;

public sealed record UpdateOwnerBannerCommand(Guid BannerId, BannerInputData Banner) : ICommand<BannerDto>;

public sealed record DeleteOwnerBannerCommand(Guid BannerId) : ICommand<bool>;

public sealed record UpdateOwnerAboutCommand(string About) : ICommand<ShopContentDto>;

internal sealed class BannerInputValidator : AbstractValidator<BannerInputData>
{
    public BannerInputValidator()
    {
        RuleFor(banner => banner.Title).NotEmpty().WithMessage("Nhập tiêu đề.").MaximumLength(ShopBanner.TitleMaxLength).WithMessage("Tiêu đề tối đa 40 ký tự.");
        RuleFor(banner => banner.Body).MaximumLength(ShopBanner.BodyMaxLength).WithMessage("Dòng phụ tối đa 80 ký tự.");
        RuleFor(banner => banner.SortOrder).GreaterThanOrEqualTo(0).WithMessage("Thứ tự là số nguyên từ 0 trở lên.");
    }
}

internal sealed class CreateOwnerBannerCommandValidator : AbstractValidator<CreateOwnerBannerCommand>
{
    public CreateOwnerBannerCommandValidator() => RuleFor(command => command.Banner).NotNull().SetValidator(new BannerInputValidator());
}

internal sealed class UpdateOwnerBannerCommandValidator : AbstractValidator<UpdateOwnerBannerCommand>
{
    public UpdateOwnerBannerCommandValidator() => RuleFor(command => command.Banner).NotNull().SetValidator(new BannerInputValidator());
}

internal sealed class UpdateOwnerAboutCommandValidator : AbstractValidator<UpdateOwnerAboutCommand>
{
    public UpdateOwnerAboutCommandValidator() => RuleFor(command => command.About).NotNull().MaximumLength(ShopAbout.MaxLength)
        .WithMessage("Đoạn giới thiệu tối đa 300 ký tự.");
}

internal static class ShopMapping
{
    public static BannerDto ToDto(this ShopBanner banner) =>
        new(banner.Id, banner.Title, banner.Body, banner.ImageUrl, banner.LinkDishId, banner.SortOrder, banner.IsActive);

    public static IEnumerable<ShopBanner> Ordered(IEnumerable<ShopBanner> banners) =>
        banners.OrderBy(banner => banner.SortOrder).ThenBy(banner => banner.Title, StringComparer.Ordinal);

    public static string? AboutText(ShopAbout? about) => string.IsNullOrEmpty(about?.About) ? null : about.About;
}

internal sealed class GetShopContentQueryHandler(IShopStore store) : IQueryHandler<GetShopContentQuery, ShopContentDto>
{
    public async Task<ShopContentDto> Handle(GetShopContentQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<ShopBanner> banners = await store.ListBannersAsync(null, cancellationToken);
        ShopAbout? about = await store.FindAboutAsync(null, cancellationToken);
        return new([.. ShopMapping.Ordered(banners.Where(banner => banner.IsActive)).Select(banner => banner.ToDto())], ShopMapping.AboutText(about));
    }
}

internal sealed class ListOwnerBannersQueryHandler(ICurrentUser currentUser, IShopStore store) : IQueryHandler<ListOwnerBannersQuery, IReadOnlyList<BannerDto>>
{
    public async Task<IReadOnlyList<BannerDto>> Handle(ListOwnerBannersQuery query, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.OwnerId ?? throw new ForbiddenException();
        return [.. ShopMapping.Ordered(await store.ListBannersAsync(ownerId, cancellationToken)).Select(banner => banner.ToDto())];
    }
}

internal sealed class CreateOwnerBannerCommandHandler(ICurrentUser currentUser, IShopStore store, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<CreateOwnerBannerCommand, BannerDto>
{
    public async Task<BannerDto> Handle(CreateOwnerBannerCommand command, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.OwnerId ?? throw new ForbiddenException();
        ShopBanner banner = ShopBanner.Create(Guid.CreateVersion7(), ownerId, command.Banner.ToDetails(), clock.GetUtcNow());
        store.AddBanner(banner);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return banner.ToDto();
    }
}

internal sealed class UpdateOwnerBannerCommandHandler(ICurrentUser currentUser, IShopStore store, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<UpdateOwnerBannerCommand, BannerDto>
{
    public async Task<BannerDto> Handle(UpdateOwnerBannerCommand command, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.OwnerId ?? throw new ForbiddenException();
        ShopBanner banner = await store.FindBannerAsync(command.BannerId, cancellationToken) is { } found && found.OwnerId == ownerId
            ? found : throw new NotFoundException();
        banner.Apply(command.Banner.ToDetails(), clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return banner.ToDto();
    }
}

internal sealed class DeleteOwnerBannerCommandHandler(ICurrentUser currentUser, IShopStore store, IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteOwnerBannerCommand, bool>
{
    public async Task<bool> Handle(DeleteOwnerBannerCommand command, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.OwnerId ?? throw new ForbiddenException();
        ShopBanner banner = await store.FindBannerAsync(command.BannerId, cancellationToken) is { } found && found.OwnerId == ownerId
            ? found : throw new NotFoundException();
        store.RemoveBanner(banner);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}

internal sealed class UpdateOwnerAboutCommandHandler(ICurrentUser currentUser, IShopStore store, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<UpdateOwnerAboutCommand, ShopContentDto>
{
    public async Task<ShopContentDto> Handle(UpdateOwnerAboutCommand command, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.OwnerId ?? throw new ForbiddenException();
        ShopAbout? about = await store.FindAboutAsync(ownerId, cancellationToken);
        if (about is null) store.AddAbout(about = ShopAbout.Create(ownerId, command.About, clock.GetUtcNow()));
        else about.Set(command.About, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        IReadOnlyList<ShopBanner> banners = await store.ListBannersAsync(ownerId, cancellationToken);
        return new([.. ShopMapping.Ordered(banners.Where(banner => banner.IsActive)).Select(banner => banner.ToDto())], ShopMapping.AboutText(about));
    }
}
