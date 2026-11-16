using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QueryDoctor.LabTests;

namespace QueryDoctor.AcceptanceTests;

/// <summary>Pasos de usuario por la Api pública, como los haría la SPA. Nada de esto entra a la base por debajo.</summary>
internal static class Platform
{
    /// <summary>Tiempo máximo para que el Worker termine un trabajo encolado (sondeo, ingesta + análisis, explain).</summary>
    public static readonly TimeSpan JobTimeout = TimeSpan.FromSeconds(120);

    /// <summary>Registra una conexión con autenticación SQL y TLS obligatorio (RNF-SEG-03). Devuelve su id.</summary>
    public static async Task<Guid> RegisterAsync(HttpClient dba, string server, string database, string user, string password)
    {
        using var response = await dba.PostAsJsonAsync("/connections", new
        {
            platform = "SqlServer",
            server,
            databaseName = database,
            authenticationMethod = "SqlAuthentication",
            encryption = "Mandatory",
            username = user,
            password,
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pending", body.GetProperty("status").GetString());
        return body.GetProperty("id").GetGuid();
    }

    /// <summary>Espera a que el Worker sondee la conexión y devuelve el detalle de <c>GET /connections/{id}</c>.</summary>
    public static Task<JsonElement> ProbedConnectionAsync(HttpClient client, Guid id) =>
        UntilAsync(
            () => GetJsonAsync(client, $"/connections/{id}"),
            detail => detail.GetProperty("status").GetString() != "Pending",
            $"el sondeo de {id}");

    public static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path} respondió {(int)response.StatusCode}.");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Repite la lectura hasta que se cumpla la condición o venza <see cref="JobTimeout"/>.</summary>
    public static async Task<T> UntilAsync<T>(Func<Task<T>> read, Func<T, bool> done, string what)
    {
        var deadline = DateTimeOffset.UtcNow + JobTimeout;
        while (true)
        {
            var value = await read();
            if (done(value))
            {
                return value;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException($"Se agotó el tiempo esperando {what} ({JobTimeout.TotalSeconds} s).");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }

    /// <summary>La contraseña común de las cuentas del laboratorio (04 y 11 la toman de READER_PASSWORD).</summary>
    public static string LabPassword => LabEnvironment.ReaderPassword();
}
