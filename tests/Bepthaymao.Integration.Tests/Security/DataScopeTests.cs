using Bepthaymao.Api.Security;
using Microsoft.AspNetCore.Http;

namespace Bepthaymao.Integration.Tests.Security;

/// <summary>
/// Phạm vi dữ liệu phải fail-closed: thiếu HttpContext không tự thành quyền hệ thống
/// (review 01.1 Important #2, phase 01.2 D-09).
/// </summary>
public sealed class DataScopeTests
{
    [Fact]
    public void ScopeOutsideRequestWithoutGrantIsNotSystem()
    {
        HttpCurrentUser user = new(new HttpContextAccessor(), new SystemScopeGrant());

        Assert.False(user.IsSystem);
        Assert.Null(user.OwnerId);
        Assert.Null(user.BranchId);
    }

    [Fact]
    public void ScopeOutsideRequestWithExplicitGrantIsSystem()
    {
        HttpCurrentUser user = new(new HttpContextAccessor(), new SystemScopeGrant { Granted = true });

        Assert.True(user.IsSystem);
    }

    [Fact]
    public void GrantNeverWidensAnHttpRequest()
    {
        HttpContextAccessor accessor = new() { HttpContext = new DefaultHttpContext() };
        HttpCurrentUser user = new(accessor, new SystemScopeGrant { Granted = true });

        Assert.False(user.IsSystem);
    }
}
