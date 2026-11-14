using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Persistence;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Domain.Shop;
using FluentValidation;

namespace Bepthaymao.Application.Shop;

public interface ILegalStore
{
    /// <summary><paramref name="ownerId"/> null lấy bản cập nhật mới nhất của bất kỳ Owner (luồng công khai).</summary>
    Task<ShopLegal?> FindLegalAsync(Guid? ownerId, CancellationToken cancellationToken);
    void AddLegal(ShopLegal legal);
    /// <summary><paramref name="ownerId"/> null là mọi Owner (luồng công khai).</summary>
    Task<IReadOnlyList<ShopPolicy>> ListPoliciesAsync(Guid? ownerId, CancellationToken cancellationToken);
    void AddPolicy(ShopPolicy policy);
}

public sealed record PolicySummaryDto(PolicyKind Kind, string Title);

public sealed record PolicyDto(PolicyKind Kind, string Title, string Body, DateTimeOffset? UpdatedAt);

/// <summary>Chân trang công khai: thông tin người bán (trường chưa nhập là null) và các chính sách đã có nội dung.</summary>
public sealed record ShopLegalDto(string? BusinessName, string? RegistrationNumber, DateOnly? RegistrationIssuedOn,
    string? RegistrationIssuedBy, string? Address, string? Phone, string? Email, string? MoitNoticeUrl,
    IReadOnlyList<PolicySummaryDto> Policies);

/// <summary>Bản cho chủ quán sửa: đủ sáu chính sách, chính sách chưa viết có body rỗng.</summary>
public sealed record OwnerLegalDto(string? BusinessName, string? RegistrationNumber, DateOnly? RegistrationIssuedOn,
    string? RegistrationIssuedBy, string? Address, string? Phone, string? Email, string? MoitNoticeUrl,
    IReadOnlyList<PolicyDto> Policies);

public sealed record GetShopLegalQuery : IQuery<ShopLegalDto>;

public sealed record GetShopPolicyQuery(PolicyKind Kind) : IQuery<PolicyDto>;

public sealed record GetOwnerLegalQuery : IQuery<OwnerLegalDto>;

public sealed record UpdateOwnerLegalCommand(LegalDetails Legal) : ICommand<OwnerLegalDto>;

public sealed record UpdateOwnerPolicyCommand(PolicyKind Kind, string Body) : ICommand<PolicyDto>;

public static class PolicyTitles
{
    public static readonly IReadOnlyList<PolicyKind> Order =
        [PolicyKind.Terms, PolicyKind.Privacy, PolicyKind.Delivery, PolicyKind.Payment, PolicyKind.Refund, PolicyKind.Complaint];

    public static string Of(PolicyKind kind) => kind switch
    {
        PolicyKind.Terms => "Điều khoản sử dụng",
        PolicyKind.Privacy => "Chính sách bảo mật thông tin",
        PolicyKind.Delivery => "Chính sách giao nhận",
        PolicyKind.Payment => "Chính sách thanh toán",
        PolicyKind.Refund => "Chính sách đổi trả và hoàn tiền",
        PolicyKind.Complaint => "Giải quyết khiếu nại",
        _ => kind.ToString(),
    };
}

internal sealed class UpdateOwnerLegalCommandValidator : AbstractValidator<UpdateOwnerLegalCommand>
{
    public UpdateOwnerLegalCommandValidator()
    {
        RuleFor(command => command.Legal).NotNull();
        RuleFor(command => command.Legal.BusinessName).MaximumLength(ShopLegal.NameMaxLength).WithMessage("Tên người bán tối đa 200 ký tự.");
        RuleFor(command => command.Legal.RegistrationNumber).MaximumLength(ShopLegal.RegistrationMaxLength).WithMessage("Số đăng ký tối đa 50 ký tự.");
        RuleFor(command => command.Legal.RegistrationIssuedBy).MaximumLength(ShopLegal.IssuedByMaxLength).WithMessage("Nơi cấp tối đa 200 ký tự.");
        RuleFor(command => command.Legal.Address).MaximumLength(ShopLegal.AddressMaxLength).WithMessage("Địa chỉ tối đa 300 ký tự.");
        RuleFor(command => command.Legal.Phone).MaximumLength(ShopLegal.PhoneMaxLength).WithMessage("Số điện thoại tối đa 20 ký tự.");
        RuleFor(command => command.Legal.Email).MaximumLength(ShopLegal.EmailMaxLength).EmailAddress()
            .When(command => !string.IsNullOrWhiteSpace(command.Legal.Email)).WithMessage("Email không hợp lệ.");
    }
}

internal sealed class UpdateOwnerPolicyCommandValidator : AbstractValidator<UpdateOwnerPolicyCommand>
{
    public UpdateOwnerPolicyCommandValidator()
    {
        RuleFor(command => command.Kind).IsInEnum().WithMessage("Loại chính sách không hợp lệ.");
        RuleFor(command => command.Body).NotNull().MaximumLength(ShopPolicy.BodyMaxLength).WithMessage("Nội dung chính sách tối đa 20.000 ký tự.");
    }
}

internal static class LegalMapping
{
    public static PolicyDto ToDto(this ShopPolicy policy) => new(policy.Kind, PolicyTitles.Of(policy.Kind), policy.Body, policy.UpdatedAt);

    /// <summary>Mỗi loại lấy bản mới nhất có nội dung.</summary>
    public static IReadOnlyList<ShopPolicy> Latest(IEnumerable<ShopPolicy> policies) =>
        [.. policies.Where(policy => policy.Body.Length > 0).GroupBy(policy => policy.Kind)
            .Select(group => group.OrderByDescending(policy => policy.UpdatedAt).First())
            .OrderBy(policy => PolicyTitles.Order.ToList().IndexOf(policy.Kind))];

    public static OwnerLegalDto ToOwnerDto(ShopLegal? legal, IReadOnlyList<ShopPolicy> policies) => new(
        legal?.BusinessName, legal?.RegistrationNumber, legal?.RegistrationIssuedOn, legal?.RegistrationIssuedBy,
        legal?.Address, legal?.Phone, legal?.Email, legal?.MoitNoticeUrl,
        [.. PolicyTitles.Order.Select(kind => policies.FirstOrDefault(policy => policy.Kind == kind)?.ToDto()
            ?? new PolicyDto(kind, PolicyTitles.Of(kind), "", null))]);
}

internal sealed class GetShopLegalQueryHandler(ILegalStore store) : IQueryHandler<GetShopLegalQuery, ShopLegalDto>
{
    public async Task<ShopLegalDto> Handle(GetShopLegalQuery query, CancellationToken cancellationToken)
    {
        ShopLegal? legal = await store.FindLegalAsync(null, cancellationToken);
        IReadOnlyList<ShopPolicy> policies = LegalMapping.Latest(await store.ListPoliciesAsync(legal?.OwnerId, cancellationToken));
        return new(legal?.BusinessName, legal?.RegistrationNumber, legal?.RegistrationIssuedOn, legal?.RegistrationIssuedBy,
            legal?.Address, legal?.Phone, legal?.Email, legal?.MoitNoticeUrl,
            [.. policies.Select(policy => new PolicySummaryDto(policy.Kind, PolicyTitles.Of(policy.Kind)))]);
    }
}

internal sealed class GetShopPolicyQueryHandler(ILegalStore store) : IQueryHandler<GetShopPolicyQuery, PolicyDto>
{
    public async Task<PolicyDto> Handle(GetShopPolicyQuery query, CancellationToken cancellationToken)
    {
        ShopLegal? legal = await store.FindLegalAsync(null, cancellationToken);
        IReadOnlyList<ShopPolicy> policies = LegalMapping.Latest(await store.ListPoliciesAsync(legal?.OwnerId, cancellationToken));
        return policies.FirstOrDefault(policy => policy.Kind == query.Kind)?.ToDto() ?? throw new NotFoundException();
    }
}

internal sealed class GetOwnerLegalQueryHandler(ICurrentUser currentUser, ILegalStore store) : IQueryHandler<GetOwnerLegalQuery, OwnerLegalDto>
{
    public async Task<OwnerLegalDto> Handle(GetOwnerLegalQuery query, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.OwnerId ?? throw new ForbiddenException();
        return LegalMapping.ToOwnerDto(await store.FindLegalAsync(ownerId, cancellationToken), await store.ListPoliciesAsync(ownerId, cancellationToken));
    }
}

internal sealed class UpdateOwnerLegalCommandHandler(ICurrentUser currentUser, ILegalStore store, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<UpdateOwnerLegalCommand, OwnerLegalDto>
{
    public async Task<OwnerLegalDto> Handle(UpdateOwnerLegalCommand command, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.OwnerId ?? throw new ForbiddenException();
        ShopLegal? legal = await store.FindLegalAsync(ownerId, cancellationToken);
        if (legal is null) store.AddLegal(legal = ShopLegal.Create(ownerId, command.Legal, clock.GetUtcNow()));
        else legal.Apply(command.Legal, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return LegalMapping.ToOwnerDto(legal, await store.ListPoliciesAsync(ownerId, cancellationToken));
    }
}

internal sealed class UpdateOwnerPolicyCommandHandler(ICurrentUser currentUser, ILegalStore store, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<UpdateOwnerPolicyCommand, PolicyDto>
{
    public async Task<PolicyDto> Handle(UpdateOwnerPolicyCommand command, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.OwnerId ?? throw new ForbiddenException();
        ShopPolicy? policy = (await store.ListPoliciesAsync(ownerId, cancellationToken)).FirstOrDefault(item => item.Kind == command.Kind);
        if (policy is null) store.AddPolicy(policy = ShopPolicy.Create(ownerId, command.Kind, command.Body, clock.GetUtcNow()));
        else policy.Set(command.Body, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return policy.ToDto();
    }
}
