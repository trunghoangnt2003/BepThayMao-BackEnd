using Bepthaymao.Application.Abstractions.Security;

namespace Bepthaymao.Infrastructure.Persistence;

/// <summary>Phạm vi hệ thống tường minh cho CLI, seed và design-time: thấy mọi dữ liệu cơ sở.</summary>
public sealed class SystemDataScope : IDataScope
{
    public bool IsSystem => true;
    public Guid? OwnerId => null;
    public Guid? BranchId => null;
}
