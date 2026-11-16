using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using QueryDoctor.LabTests;

namespace QueryDoctor.AcceptanceTests;

/// <summary>
/// HU-01 (acceso) y HU-14 (traza y auditoría) de punta a punta, con datos reales de QD_Lab: sin sesión nada se ve, el
/// Desarrollador creado por el DBA solo ve lo asignado (RNF-SEG-12, filtro en la capa de datos) y todo queda auditado.
/// </summary>
[Collection(AcceptanceGroup.Name)]
[Trait("Suite", "Aceptacion")]
public sealed class AccessAcceptanceTests(AcceptanceFixture platform)
{
    private const string DeveloperInitialPassword = "Inicial!Dev#2026";
    private const string DeveloperPassword = "Cambiada!Dev#2026";

    // CA-HU01.5: sin sesión válida se deniega el acceso a conexiones, consultas, planes, hallazgos, explicaciones y traza.
    [LabFact]
    [Trait("Criterio", "CA-HU01.5")]
    public async Task SinSesionTodoSeDeniega()
    {
        var lab = await platform.QdLabAsync();
        var queryId = await AnalysisAcceptanceTests.Esc01Async(lab);
        var planId = (await AnalysisAcceptanceTests.DetailAsync(lab, queryId)).GetProperty("plans")[0].GetProperty("planId").GetGuid();
        using var anonymous = platform.Api.AnonymousClient();

        string[] paths =
        [
            "/connections", $"/connections/{lab.Id}", $"/connections/{lab.Id}/ingestions",
            $"/queries?connectionId={lab.Id}", $"/queries/{queryId}", $"/queries/{queryId}/plans/{planId}",
            $"/queries/{queryId}/explanations/latest", "/trace", "/audit",
        ];
        foreach (var path in paths)
        {
            using var response = await anonymous.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"GET {path} sin sesión respondió {(int)response.StatusCode}.");
        }

        using var post = await anonymous.PostAsync($"/queries/{queryId}/explanations", null);
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
    }

    // CA-HU01.6: el Desarrollador no registra, no ingiere, no borra ni asigna. RNF-SEG-12: solo ve las conexiones que el
    // DBA le asignó, también por id directo, y con su asignación lee el listado, el detalle y el plan.
    [LabFact]
    [Trait("Criterio", "CA-HU01.6")]
    [Trait("Criterio", "CA-HU01.5")]
    public async Task ElDesarrolladorSoloVeLoAsignadoYNoAdministraConexiones()
    {
        var lab = await platform.QdLabAsync();
        var hidden = await platform.NoQueryStoreAsync();
        var queryId = await AnalysisAcceptanceTests.Esc01Async(lab);
        var planId = (await AnalysisAcceptanceTests.DetailAsync(lab, queryId)).GetProperty("plans")[0].GetProperty("planId").GetGuid();

        var (developer, developerId) = await CreateDeveloperAsync(lab);

        // Sin asignaciones no ve nada, ni por id directo (404 igual que lo inexistente).
        Assert.Equal(0, (await Platform.GetJsonAsync(developer, "/connections")).GetArrayLength());
        await AssertStatusAsync(developer, HttpMethod.Get, $"/queries/{queryId}", HttpStatusCode.NotFound);

        using (var assign = await lab.Dba.PutAsync($"/connections/{lab.Id}/assignments/{developerId}", null))
        {
            Assert.True(assign.IsSuccessStatusCode, $"Asignar respondió {(int)assign.StatusCode}.");
        }

        var visible = (await Platform.GetJsonAsync(developer, "/connections")).EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();
        Assert.Equal([lab.Id], visible);
        await AssertStatusAsync(developer, HttpMethod.Get, $"/connections/{hidden}", HttpStatusCode.NotFound);
        await AssertStatusAsync(developer, HttpMethod.Get, $"/connections/{hidden}/ingestions", HttpStatusCode.NotFound);
        Assert.Equal(0, (await Platform.GetJsonAsync(developer, $"/queries?connectionId={hidden}")).GetArrayLength());

        var queries = await Platform.GetJsonAsync(developer, "/queries?window=24h");
        Assert.NotEqual(0, queries.GetArrayLength());
        Assert.All(queries.EnumerateArray(), q => Assert.Equal(lab.Id, q.GetProperty("connectionId").GetGuid()));
        await Platform.GetJsonAsync(developer, $"/queries/{queryId}");
        await Platform.GetJsonAsync(developer, $"/queries/{queryId}/plans/{planId}");

        // CA-HU01.6: ninguna acción de administración, ni siquiera sobre lo asignado.
        using (var register = await developer.PostAsJsonAsync("/connections", new
        {
            platform = "SqlServer",
            server = AcceptanceFixture.Server,
            databaseName = "QD_Otra",
            authenticationMethod = "SqlAuthentication",
            encryption = "Mandatory",
            username = LabEnvironment.Reader,
            password = Platform.LabPassword,
        }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, register.StatusCode);
        }

        await AssertStatusAsync(developer, HttpMethod.Post, $"/connections/{lab.Id}/ingestions", HttpStatusCode.Forbidden);
        await AssertStatusAsync(developer, HttpMethod.Delete, $"/connections/{lab.Id}/evidence", HttpStatusCode.Forbidden);
        await AssertStatusAsync(developer, HttpMethod.Delete, $"/queries/{queryId}/evidence", HttpStatusCode.Forbidden);
        await AssertStatusAsync(developer, HttpMethod.Put, $"/connections/{hidden}/assignments/{developerId}", HttpStatusCode.Forbidden);
        await AssertStatusAsync(developer, HttpMethod.Get, "/trace", HttpStatusCode.Forbidden);
        await AssertStatusAsync(developer, HttpMethod.Get, "/audit", HttpStatusCode.Forbidden);
    }

    // CA-HU14.2: no existe ninguna operación para modificar o borrar la traza ni la auditoría.
    [LabFact]
    [Trait("Criterio", "CA-HU14.2")]
    public async Task LaTrazaYLaAuditoriaNoTienenOperacionesDeEscritura()
    {
        var lab = await platform.QdLabAsync();

        var routes = platform.Api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw
                && (raw.StartsWith("/trace", StringComparison.Ordinal) || raw.StartsWith("/audit", StringComparison.Ordinal)))
            .ToList();
        Assert.NotEmpty(routes);
        Assert.All(routes, route =>
        {
            var methods = route.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            Assert.Equal(["GET"], methods);
        });

        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            foreach (var path in new[] { "/trace", "/audit", "/trace/1", "/audit/1" })
            {
                using var request = new HttpRequestMessage(method, path);
                using var response = await lab.Dba.SendAsync(request);
                Assert.True(
                    response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound,
                    $"{method} {path} respondió {(int)response.StatusCode}.");
            }
        }
    }

    // CA-HU14.3: filtrar la traza por consulta o por rango de fechas devuelve solo esas entradas.
    [LabFact]
    [Trait("Criterio", "CA-HU14.3")]
    public async Task LaTrazaSeFiltraPorConsultaYPorFechas()
    {
        var lab = await platform.QdLabAsync();
        var esc01 = await AnalysisAcceptanceTests.Esc01Async(lab);

        // Al menos una explicación de ESC01 (la no verificada de la otra clase o esta, según el orden).
        platform.Ai.Explain = AiResponses.WrongNumber;
        using (var request = await lab.Dba.PostAsync($"/queries/{esc01}/explanations", null))
        {
            Assert.True(request.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.Conflict, $"POST respondió {(int)request.StatusCode}.");
        }

        var byQuery = await Platform.UntilAsync(
            () => Platform.GetJsonAsync(lab.Dba, $"/trace?queryId={esc01}"),
            entries => entries.GetArrayLength() > 0,
            "una entrada de traza de ESC01");
        Assert.All(byQuery.EnumerateArray(), e => Assert.Equal(esc01, e.GetProperty("query_id").GetGuid()));

        var all = await Platform.GetJsonAsync(lab.Dba, "/trace?take=500");
        var first = all.EnumerateArray().Select(e => e.GetProperty("recorded_at").GetDateTimeOffset()).Min();
        var future = DateTimeOffset.UtcNow.AddDays(1).ToString("O");
        Assert.Equal(0, (await Platform.GetJsonAsync(lab.Dba, $"/trace?from={Uri.EscapeDataString(future)}")).GetArrayLength());

        var from = first.ToString("O");
        var ranged = await Platform.GetJsonAsync(lab.Dba, $"/trace?from={Uri.EscapeDataString(from)}&take=500");
        Assert.All(ranged.EnumerateArray(), e => Assert.True(e.GetProperty("recorded_at").GetDateTimeOffset() >= first));
    }

    // CA-HU14.4: la auditoría tiene los inicios de sesión exitosos y fallidos, el registro y la edición de conexiones, las
    // ingestas manuales y los borrados, cada uno con usuario y fecha; la cadena sigue íntegra.
    // Los cambios de umbrales no tienen emisor: los umbrales desde la interfaz están fuera del MVP (Podría).
    [LabFact]
    [Trait("Criterio", "CA-HU14.4")]
    public async Task LaAuditoriaRegistraSesionesConexionesIngestasYBorrados()
    {
        var lab = await platform.QdLabAsync();
        var hidden = await platform.NoQueryStoreAsync();

        using (var anonymous = platform.Api.AnonymousClient())
        using (await anonymous.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["usuario"] = "nadie-" + Guid.NewGuid().ToString("N")[..8],
            ["password"] = "no-es-una-clave",
        })))
        {
        }

        // Un borrado a pedido sobre la conexión sin datos: queda auditado (nothing_to_delete) sin purgar QD_Lab.
        await AssertStatusAsync(lab.Dba, HttpMethod.Delete, $"/connections/{hidden}/evidence", HttpStatusCode.NoContent);

        var events = (await Platform.GetJsonAsync(lab.Dba, "/audit?take=500")).EnumerateArray().ToList();
        var types = events.Select(e => e.GetProperty("event_type").GetString()).ToHashSet();
        foreach (var expected in new[] { "LoginSucceeded", "LoginFailed", "ConnectionRegistered", "ManualIngestionRequested", "DataDeleted" })
        {
            Assert.True(types.Contains(expected), $"Falta {expected} en la auditoría: {string.Join(", ", types)}");
        }

        Assert.All(events.Where(e => e.GetProperty("event_type").GetString() is "ConnectionRegistered" or "ManualIngestionRequested" or "DataDeleted"), e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.GetProperty("user_name").GetString()));
            Assert.Equal(JsonValueKind.String, e.GetProperty("recorded_at").ValueKind);
        });

        var verify = await Platform.GetJsonAsync(lab.Dba, "/audit/verify");
        Assert.True(verify.GetProperty("intact").GetBoolean(), $"Cadena de auditoría: {verify}");
    }

    /// <summary>
    /// El DBA crea el Desarrollador por la Api (alta con cambio obligatorio), el Desarrollador entra, no lee nada hasta
    /// cambiar la contraseña (D7 de S2-12) y vuelve a entrar con la nueva.
    /// </summary>
    private async Task<(HttpClient Client, Guid Id)> CreateDeveloperAsync(LabConnection lab)
    {
        var userName = $"dev{Guid.NewGuid():N}"[..16];
        using var created = await lab.Dba.PostAsJsonAsync("/users", new
        {
            userName,
            email = $"{userName}@querydoctor.test",
            password = DeveloperInitialPassword,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var first = await platform.Api.LoginAsync(userName, DeveloperInitialPassword);
        await AssertStatusAsync(first, HttpMethod.Get, "/connections", HttpStatusCode.Forbidden);
        using (var change = await first.PostAsync("/auth/change-password", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["actual"] = DeveloperInitialPassword,
            ["nueva"] = DeveloperPassword,
            ["confirmar"] = DeveloperPassword,
        })))
        {
            Assert.Equal(HttpStatusCode.Redirect, change.StatusCode);
            Assert.Contains("changed=1", change.Headers.Location?.OriginalString, StringComparison.Ordinal);
        }

        first.Dispose();
        return (await platform.Api.LoginAsync(userName, DeveloperPassword), id);
    }

    private static async Task AssertStatusAsync(HttpClient client, HttpMethod method, string path, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, path);
        using var response = await client.SendAsync(request);
        Assert.True(response.StatusCode == expected, $"{method} {path} respondió {(int)response.StatusCode}, se esperaba {(int)expected}.");
    }
}
