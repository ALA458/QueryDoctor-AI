using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using QueryDoctor.Domain.Ops;

namespace QueryDoctor.IntegrationTests;

/// <summary>
/// S2-12 D6 (alta de usuarios Desarrollador por DBA) y OWASP A07 (límite de tasa en /auth/login).
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class UserManagementAndRateLimitTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _api = new(postgres);

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task DbaCreaUsuarioDesarrolladorConCambioObligatorioYDesarrolladorNoPuedeCrear()
    {
        using var developerClient = await _api.ClientAsync(RoleNames.Developer);
        using var forbidden = await developerClient.PostAsJsonAsync("/users", new
        {
            userName = "dev.noauth",
            email = "dev.noauth@querydoctor.test",
            password = ApiFactory.UserPassword,
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var dba = await _api.ClientAsync(RoleNames.Dba);
        var userName = $"dev.{Guid.NewGuid():N}"[..16];
        using var created = await dba.PostAsJsonAsync("/users", new
        {
            userName,
            email = $"{userName}@querydoctor.test",
            password = ApiFactory.UserPassword,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(RoleNames.Developer, body.GetProperty("role").GetString());
        Assert.True(body.GetProperty("mustChangePassword").GetBoolean());

        var listed = await dba.GetFromJsonAsync<JsonElement>($"/users?role={RoleNames.Developer}");
        Assert.Contains(listed.EnumerateArray(), u => u.GetProperty("userName").GetString() == userName);

        using var loginClient = _api.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(ApiFactory.Origin),
            AllowAutoRedirect = false,
        });
        loginClient.DefaultRequestHeaders.Add("Origin", ApiFactory.Origin);
        using var loginResponse = await loginClient.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["usuario"] = userName,
            ["password"] = ApiFactory.UserPassword,
        }));
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.StartsWith("/cambiar-contrasena", loginResponse.Headers.Location?.OriginalString, StringComparison.Ordinal);

        var audit = await dba.GetFromJsonAsync<JsonElement>("/audit?type=DeveloperUserCreated&take=50");
        Assert.Contains(audit.EnumerateArray(), e => e.GetProperty("subject_id").GetGuid() == body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task LoginAplicaLimiteDeTasaYDevuelve429()
    {
        using var limitedFactory = _api.WithWebHostBuilder(builder =>
            builder.UseSetting("Auth:LoginRateLimitPerMinute", "2"));

        using var client = limitedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(ApiFactory.Origin),
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add("Origin", ApiFactory.Origin);

        async Task<HttpResponseMessage> PostBadLoginAsync() =>
            await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["usuario"] = "inexistente",
                ["password"] = "Clave!Mala#123",
            }));

        using var first = await PostBadLoginAsync();
        using var second = await PostBadLoginAsync();
        using var third = await PostBadLoginAsync();

        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }
}
