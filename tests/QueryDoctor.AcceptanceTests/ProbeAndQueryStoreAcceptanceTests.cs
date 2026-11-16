using System.Text.Json;
using QueryDoctor.LabTests;

namespace QueryDoctor.AcceptanceTests;

/// <summary>
/// Casos negativos de HU-01 y HU-02 de punta a punta: el SQL Server real rechaza la cuenta o no tiene Query Store, y la
/// plataforma lo informa por la Api sin activar la conexión ni intentar cambiar nada en el motor.
/// </summary>
[Collection(AcceptanceGroup.Name)]
[Trait("Suite", "Aceptacion")]
public sealed class ProbeAndQueryStoreAcceptanceTests(AcceptanceFixture platform)
{
    /// <summary>Base de lab/sql/12: qd_noperm tiene usuario y ningún permiso.</summary>
    private const string NegativeDatabase = "QD_Lab_Acc";

    // CA-HU01.2: credenciales inválidas, y luego una cuenta que conecta pero sin el permiso mínimo (qd_noperm): en los
    // dos casos se muestra la causa y la conexión no se activa. Volver a registrar una Failed reemplaza las credenciales
    // y vuelve a sondear (D3 de S1-09).
    [LabFact]
    [Trait("Criterio", "CA-HU01.2")]
    public async Task CredencialesInvalidasYCuentaSinPermisoQuedanFailedConSuCausa()
    {
        var lab = await platform.QdLabAsync();

        var id = await Platform.RegisterAsync(lab.Dba, AcceptanceFixture.Server, NegativeDatabase, LabEnvironment.Reader, "no-es-la-contrasena-" + Guid.NewGuid());
        AssertFailed(await Platform.ProbedConnectionAsync(lab.Dba, id), "InvalidCredentials");

        var again = await Platform.RegisterAsync(lab.Dba, AcceptanceFixture.Server, NegativeDatabase, LabEnvironment.NoPermission, Platform.LabPassword);
        AssertFailed(await Platform.ProbedConnectionAsync(lab.Dba, again), "MissingPermission");

        // Una conexión que no está Active no se ingiere (no hay nada que leer con esa cuenta).
        using var ingest = await lab.Dba.PostAsync($"/connections/{again}/ingestions", null);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, ingest.StatusCode);
    }

    // CA-HU02.2: con Query Store deshabilitado la ingesta se omite, se informa la causa y nada se rompe. CA-HU02.3: la
    // plataforma no lo enciende.
    [LabFact]
    [Trait("Criterio", "CA-HU02.2")]
    [Trait("Criterio", "CA-HU02.3")]
    public async Task ConQueryStoreApagadoLaIngestaSeOmiteEInformaLaCausa()
    {
        var lab = await platform.QdLabAsync();
        var before = await LabSql.QueryStoreConfigurationAsync(LabEnvironment.DatabaseWithoutQueryStore);
        var id = await platform.NoQueryStoreAsync();

        var connection = await Platform.GetJsonAsync(lab.Dba, $"/connections/{id}");
        var queryStore = connection.GetProperty("capabilities").GetProperty("queryStore");
        Assert.Equal("OFF", queryStore.GetProperty("actualState").GetString());
        Assert.False(queryStore.GetProperty("canIngest").GetBoolean());

        var run = await Platform.IngestAsync(lab.Dba, id);

        Assert.Equal("Skipped", run.GetProperty("status").GetString());
        Assert.Equal("QueryStoreDisabled", run.GetProperty("cause").GetString());
        Assert.Contains("Query Store", run.GetProperty("causeMessage").GetString(), StringComparison.Ordinal);

        // La omisión no borra ni toca lo ya almacenado de otras conexiones, y la de QD_Lab sigue leyéndose.
        Assert.NotEqual(0, (await AnalysisAcceptanceTests.QueriesAsync(lab, "window=24h")).GetArrayLength());
        Assert.Equal(JsonValueKind.Null, (await Platform.GetJsonAsync(lab.Dba, $"/connections/{id}")).GetProperty("lastIngestionAt").ValueKind);

        var after = await LabSql.QueryStoreConfigurationAsync(LabEnvironment.DatabaseWithoutQueryStore);
        Assert.Equal("OFF", after["actual_state_desc"]);
        Assert.Equal(before, after);
    }

    private static void AssertFailed(JsonElement detail, string cause)
    {
        Assert.True(detail.GetProperty("status").GetString() == "Failed", $"Se esperaba Failed/{cause}: {detail}");
        Assert.Equal(cause, detail.GetProperty("failureCause").GetString());
        Assert.False(string.IsNullOrWhiteSpace(detail.GetProperty("failureMessage").GetString()));
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("capabilities").ValueKind);
    }
}
