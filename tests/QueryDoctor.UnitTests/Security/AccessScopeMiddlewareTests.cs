using System.Security.Claims;
using QueryDoctor.Api.Auth;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Domain.Ops;

namespace QueryDoctor.UnitTests.Security;

/// <summary>
/// S2-12 (RNF-SEG-12, D7): el contexto de acceso de la Api, sin la política delante. Las pruebas de integración ven el
/// 403 de <c>ConnectionReader</c>, que corta antes de que importe el contexto; aquí se prueba la segunda mitad de D7:
/// sin usuario, con cambio de contraseña pendiente, sin rol o con un rol desconocido el acceso queda en None (cero
/// filas) y ni siquiera se leen asignaciones.
/// </summary>
public sealed class AccessScopeMiddlewareTests
{
    private static readonly Guid UserId = Guid.Parse("0d7c2a51-3b9e-4f61-8c2d-5a4b3c2d1e0f");
    private static readonly Guid[] Assigned = [Guid.Parse("6f1e2d3c-4b5a-4968-8776-655443322110")];

    [Fact]
    public async Task SinSesionQuedaEnNoneSinLeerAsignaciones()
    {
        var (scope, reader) = await InitializeAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        AssertNone(scope);
        Assert.Equal(0, reader.Calls);
    }

    [Theory]
    [InlineData(RoleNames.Developer)]
    [InlineData(RoleNames.Dba)]
    public async Task ConCambioDeContrasenaPendienteQuedaEnNone(string role)
    {
        var (scope, reader) = await InitializeAsync(User([role], mustChangePassword: true));

        AssertNone(scope);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task SinRolQuedaEnNone()
    {
        var (scope, reader) = await InitializeAsync(User([]));

        AssertNone(scope);
        Assert.Equal(0, reader.Calls);
    }

    [Theory]
    [InlineData("Auditor")]
    [InlineData("desarrollador")] // Los roles se comparan exactos: otra grafía es un rol desconocido.
    public async Task UnRolDesconocidoQuedaEnNone(string role)
    {
        var (scope, reader) = await InitializeAsync(User([role]));

        AssertNone(scope);
        Assert.Equal(0, reader.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("no-es-un-guid")]
    public async Task UnDesarrolladorSinIdValidoQuedaEnNone(string? nameIdentifier)
    {
        var (scope, reader) = await InitializeAsync(User([RoleNames.Developer], nameIdentifier: nameIdentifier));

        AssertNone(scope);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task UnDesarrolladorVeSoloLoQueLeeDeSusAsignaciones()
    {
        var (scope, reader) = await InitializeAsync(User([RoleNames.Developer]));

        Assert.Equal(AccessKind.Developer, scope.Kind);
        Assert.Equal(Assigned, scope.AssignedConnectionIds);
        Assert.Equal([UserId], reader.RequestedFor);
    }

    [Fact]
    public async Task ElDbaVeTodoSinConsultaExtra()
    {
        var (scope, reader) = await InitializeAsync(User([RoleNames.Dba]));

        Assert.Equal(AccessKind.Dba, scope.Kind);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task ConLosDosRolesGanaDba()
    {
        var (scope, reader) = await InitializeAsync(User([RoleNames.Developer, RoleNames.Dba]));

        Assert.Equal(AccessKind.Dba, scope.Kind);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public void ElContextoSoloSeInicializaUnaVez()
    {
        var scope = new RequestAccessScope();
        scope.InitializeDeveloper([]);

        Assert.Throws<InvalidOperationException>(scope.InitializeDba);
        Assert.Equal(AccessKind.Developer, scope.Kind);
    }

    [Fact]
    public void SinInicializarEsNone()
    {
        AssertNone(new RequestAccessScope());
    }

    private static void AssertNone(RequestAccessScope scope)
    {
        Assert.Equal(AccessKind.None, scope.Kind);
        Assert.Empty(scope.AssignedConnectionIds);
    }

    private static async Task<(RequestAccessScope Scope, RecordingReader Reader)> InitializeAsync(ClaimsPrincipal user)
    {
        var scope = new RequestAccessScope();
        var reader = new RecordingReader();
        await AccessScopeMiddleware.InitializeAsync(user, scope, reader, CancellationToken.None);
        return (scope, reader);
    }

    private static ClaimsPrincipal User(string[] roles, bool mustChangePassword = false, string? nameIdentifier = "default")
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "usuario") };
        if (nameIdentifier is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, nameIdentifier == "default" ? UserId.ToString() : nameIdentifier));
        }

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        if (mustChangePassword)
        {
            claims.Add(new Claim(AuthConstants.MustChangePasswordClaim, "1"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Prueba"));
    }

    private sealed class RecordingReader : IAssignedConnectionsReader
    {
        public List<Guid> RequestedFor { get; } = [];

        public int Calls => RequestedFor.Count;

        public Task<Guid[]> ForUserAsync(Guid userId, CancellationToken cancellationToken)
        {
            RequestedFor.Add(userId);
            return Task.FromResult(Assigned);
        }
    }
}
