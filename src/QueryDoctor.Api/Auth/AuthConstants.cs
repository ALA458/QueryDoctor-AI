namespace QueryDoctor.Api.Auth;

/// <summary>Nombres de políticas y claims de autenticación.</summary>
internal static class AuthConstants
{
    /// <summary>Solo usuarios con rol DBA y sin cambio de contraseña pendiente (RNF-SEG-12).</summary>
    public const string DbaOnlyPolicy = "DbaOnly";

    /// <summary>Cualquier sesión válida, incluso con cambio de contraseña pendiente.</summary>
    public const string SessionPolicy = "Session";

    /// <summary>Claim que marca una sesión que solo puede cambiar la contraseña.</summary>
    public const string MustChangePasswordClaim = "qd:must_change_password";

    public const string LoginPage = "/login";

    public const string ChangePasswordPage = "/cambiar-contrasena";
}
