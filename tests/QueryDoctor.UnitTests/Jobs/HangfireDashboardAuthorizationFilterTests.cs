using System.Security.Claims;
using QueryDoctor.Api.Auth;
using QueryDoctor.Domain.Ops;

namespace QueryDoctor.UnitTests.Jobs;

public class HangfireDashboardAuthorizationFilterTests
{
    [Fact]
    public void IsAuthorizedReturnsFalseWhenUserIsNull()
    {
        var result = HangfireDashboardAuthorizationFilter.IsAuthorized(null);

        Assert.False(result);
    }

    [Fact]
    public void IsAuthorizedReturnsFalseWhenUserIsNotAuthenticated()
    {
        var identity = new ClaimsIdentity(); // Not authenticated
        var principal = new ClaimsPrincipal(identity);

        var result = HangfireDashboardAuthorizationFilter.IsAuthorized(principal);

        Assert.False(result);
    }

    [Fact]
    public void IsAuthorizedReturnsFalseWhenUserIsNotDbaRole()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "developer_user"), new Claim(ClaimTypes.Role, "Developer")],
            "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var result = HangfireDashboardAuthorizationFilter.IsAuthorized(principal);

        Assert.False(result);
    }

    [Fact]
    public void IsAuthorizedReturnsFalseWhenDbaMustChangePassword()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "dba_user"),
                new Claim(ClaimTypes.Role, RoleNames.Dba),
                new Claim(AuthConstants.MustChangePasswordClaim, "1")
            ],
            "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var result = HangfireDashboardAuthorizationFilter.IsAuthorized(principal);

        Assert.False(result);
    }

    [Fact]
    public void IsAuthorizedReturnsTrueWhenUserIsDbaWithoutPendingPasswordChange()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "dba_user"),
                new Claim(ClaimTypes.Role, RoleNames.Dba)
            ],
            "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var result = HangfireDashboardAuthorizationFilter.IsAuthorized(principal);

        Assert.True(result);
    }
}
