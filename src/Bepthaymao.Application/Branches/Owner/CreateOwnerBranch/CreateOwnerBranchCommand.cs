using System.Globalization;
using Bepthaymao.Application.Abstractions.Errors;
using Bepthaymao.Application.Abstractions.Messaging;
using Bepthaymao.Application.Abstractions.Persistence;
using Bepthaymao.Application.Abstractions.Security;
using Bepthaymao.Application.Auth;
using Bepthaymao.Domain.Branches;

namespace Bepthaymao.Application.Branches.Owner.CreateOwnerBranch;

public sealed record BranchOwnerSettingsData(string Name, string Address, string Phone, double Lat, double Lng, string OpenTime,
    string CloseTime, int AbandonedThresholdMinutes, IReadOnlyList<string> ZaloRecipientIds, string? BankAccountNo,
    string? BankCode, string? SepayAccountRef, bool IsActive);
public sealed record ManagerAccountData(string Identifier, string Password);
public sealed record CreateOwnerBranchCommand(BranchOwnerSettingsData Branch, ManagerAccountData Manager) : ICommand<BranchResponse>;

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
        repository.Add(branch);
        string managerIdentifier = await managers.CreateManagerAsync(branch.Id, command.Manager.Identifier, command.Manager.Password, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return BranchResponse.From(branch, managerIdentifier);
    }
}
