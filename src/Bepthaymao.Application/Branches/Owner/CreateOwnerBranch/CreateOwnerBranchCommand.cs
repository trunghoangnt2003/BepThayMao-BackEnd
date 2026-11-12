using System.Globalization;
using System.Text.RegularExpressions;
using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Persistence;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Application.Auth;
using Bepthaymao.Domain.Branches;
using FluentValidation;

namespace Bepthaymao.Application.Branches.Owner.CreateOwnerBranch;

public sealed record BranchOwnerSettingsData(string Name, string Address, string Phone, double Lat, double Lng, string OpenTime,
    string CloseTime, int AbandonedThresholdMinutes, IReadOnlyList<string> ZaloRecipientIds, string? BankAccountNo,
    string? BankCode, string? SepayAccountRef, bool IsActive) : IBranchSettingsFields;
public sealed record ManagerAccountData(string Identifier, string Password);
public sealed record CreateOwnerBranchCommand(BranchOwnerSettingsData Branch, ManagerAccountData Manager) : ICommand<BranchResponse>;

internal sealed partial class CreateOwnerBranchCommandValidator : AbstractValidator<CreateOwnerBranchCommand>
{
    public CreateOwnerBranchCommandValidator()
    {
        RuleFor(command => command.Branch).NotNull().WithMessage("Thiếu thông tin cơ sở.");
        RuleFor(command => command.Manager).NotNull().WithMessage("Thiếu thông tin tài khoản quản lý.");
        When(command => command.Branch is not null, () =>
            RuleFor(command => command.Branch).SetValidator(new BranchOwnerSettingsValidator()));
        When(command => command.Manager is not null, () =>
        {
            RuleFor(command => command.Manager.Identifier).Must(identifier => identifier is not null && IdentifierPattern().IsMatch(identifier))
                .WithMessage("Tên đăng nhập gồm 3 đến 64 ký tự: chữ, số và . _ @ + -");
            RuleFor(command => command.Manager.Password).NotEmpty().MinimumLength(10).MaximumLength(128)
                .WithMessage("Mật khẩu phải từ 10 đến 128 ký tự.");
        });
    }

    [GeneratedRegex(@"^[A-Za-z0-9._@+-]{3,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}

internal sealed class CreateOwnerBranchCommandHandler(ICurrentUser currentUser, IBranchRepository repository,
    IManagerAccountService managers, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<CreateOwnerBranchCommand, BranchResponse>
{
    public async Task<BranchResponse> Handle(CreateOwnerBranchCommand command, CancellationToken cancellationToken)
    {
        Guid ownerId = currentUser.OwnerId ?? throw new ForbiddenException();
        BranchOwnerSettingsData data = command.Branch;
        BranchDetails details = new(data.Name, data.Address, data.Phone, data.Lat, data.Lng,
            TimeOnly.ParseExact(data.OpenTime, "HH:mm", CultureInfo.InvariantCulture),
            TimeOnly.ParseExact(data.CloseTime, "HH:mm", CultureInfo.InvariantCulture),
            data.AbandonedThresholdMinutes, data.ZaloRecipientIds, data.BankAccountNo, data.BankCode, data.SepayAccountRef, data.IsActive);
        Branch branch = Branch.Create(Guid.CreateVersion7(), ownerId, details, clock.GetUtcNow());
        string? managerIdentifier = null;
        // UserManager tự gọi SaveChanges trên cùng DbContext: cơ sở, user và user_roles cùng nằm trong transaction này.
        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            repository.Add(branch);
            managerIdentifier = await managers.CreateManagerAsync(branch.Id, command.Manager.Identifier, command.Manager.Password, token);
            await unitOfWork.SaveChangesAsync(token);
        }, cancellationToken);
        return BranchResponse.From(branch, managerIdentifier);
    }
}
