namespace QueryDoctor.Api.Auth;

/// <summary>Nombres de políticas y claims de autenticación.</summary>
public static class AuthConstants
{
    /// <summary>Solo usuarios con rol DBA y sin cambio de contraseña pendiente (RNF-SEG-12).</summary>
    public const string DbaOnlyPolicy = "DbaOnly";

    /// <summary>
    /// DBA o Desarrollador, sin cambio de contraseña pendiente (S2-12, RNF-SEG-12). Solo abre la puerta: qué conexiones
    /// ve cada uno lo decide el filtro por conexión de la capa de datos, nunca el endpoint.
    /// </summary>
    public const string ConnectionReaderPolicy = "ConnectionReader";

    /// <summary>Cualquier sesión válida, incluso con cambio de contraseña pendiente.</summary>
    public const string SessionPolicy = "Session";

    /// <summary>Claim que marca una sesión que solo puede cambiar la contraseña.</summary>
    public const string MustChangePasswordClaim = "qd:must_change_password";

    public const string LoginPage = "/login";

    public const string ChangePasswordPage = "/cambiar-contrasena";
}
