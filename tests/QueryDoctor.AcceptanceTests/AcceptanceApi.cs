using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueryDoctor.Infrastructure.Persistence;

namespace QueryDoctor.AcceptanceTests;

/// <summary>
/// La Api real (Program) sobre el PostgreSQL de la suite. Solo encola: nunca habla con SQL Server ni con la IA
/// (invariante 2); el trabajo lo hace el Worker de <see cref="AcceptanceFixture"/>.
/// </summary>
internal sealed class AcceptanceApi(string postgres, string masterKey) : WebApplicationFactory<Program>
{
    public const string Origin = "https://localhost";
    public const string UserPassword = "Acept4cion!S3-06#";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:QueryDoctor", postgres);
        builder.UseSetting("Database:MigrateOnStartup", "true");

        // La misma clave maestra que el Worker: la Api cifra las credenciales y el Worker las descifra (RNF-SEG-04).
        builder.UseSetting("Security:SecretProtector:MasterKey", masterKey);
    }

    /// <summary>Sesión HTTPS (la cookie es Secure) de un usuario nuevo con el rol indicado; se crea con Identity.</summary>
    public async Task<(HttpClient Client, Guid UserId)> SessionAsync(string role)
    {
        var userName = $"acc{Guid.NewGuid():N}"[..20];
        Guid userId;
        using (var scope = Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = userName,
                Email = $"{userName}@querydoctor.test",
                EmailConfirmed = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            Assert.True((await users.CreateAsync(user, UserPassword)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            userId = user.Id;
        }

        return (await LoginAsync(userName, UserPassword), userId);
    }

    /// <summary>Inicia sesión por <c>/auth/login</c>, como lo haría la SPA.</summary>
    public async Task<HttpClient> LoginAsync(string userName, string password)
    {
        var client = AnonymousClient();
        using var login = await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["usuario"] = userName,
            ["password"] = password,
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    public HttpClient AnonymousClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(Origin),
            AllowAutoRedirect = false,
        });

        // CSRF por mismo origen en toda escritura (S1-09).
        client.DefaultRequestHeaders.Add("Origin", Origin);
        return client;
    }
}
