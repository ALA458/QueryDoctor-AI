using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using QueryDoctor.Infrastructure.Persistence;

namespace QueryDoctor.Api.Auth;

/// <summary>
/// Agrega el claim de cambio de contraseña pendiente cada vez que Identity construye el principal (RNF-SEG-13).
/// </summary>
/// <remarks>
/// S1-09 (fix de S1-06): con <c>SecurityStampValidatorOptions.ValidationInterval = 0</c> Identity reconstruye el
/// principal en cada petición con este factory. Antes el claim solo se agregaba al iniciar sesión, así que se perdía
/// en la segunda petición y la sesión restringida pasaba DbaOnly. Aquí el claim sale del valor real en la base.
/// </remarks>
internal sealed class MustChangePasswordClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        if (user.MustChangePassword)
        {
            identity.AddClaim(new Claim(AuthConstants.MustChangePasswordClaim, "1"));
        }

        return identity;
    }
}
