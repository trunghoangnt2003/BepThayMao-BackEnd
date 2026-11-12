using Microsoft.AspNetCore.Identity;

namespace Bepthaymao.Infrastructure.Identity;

public sealed class AppUser : IdentityUser<Guid>
{
    public Guid? OwnerId { get; set; }
    public Guid? BranchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
