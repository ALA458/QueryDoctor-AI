using QueryDoctor.LabTests;
using Xunit.Abstractions;

namespace QueryDoctor.AcceptanceTests;

/// <summary>
/// HU-01 y HU-02 de punta a punta: el DBA registra por la Api, la Api encola probe_connection y el Worker sondea el SQL
/// Server real con la cuenta de mínimo privilegio. Cada prueba lleva el criterio en el trait "Criterio".
/// </summary>
[Collection(AcceptanceGroup.Name)]
[Trait("Suite", "Aceptacion")]
public sealed class ConnectionAcceptanceTests(AcceptanceFixture platform, ITestOutputHelper output)
{
    // CA-HU01.1: confirma la conectividad y muestra la versión detectada. CA-HU02.1: estado, intervalo, captura y
    // retención de Query Store. RF-CFG-07: nivel de soporte de la matriz. CA-HU15.1: con solo VIEW DATABASE STATE.
    [LabFact]
    [Trait("Criterio", "CA-HU01.1")]
    [Trait("Criterio", "CA-HU02.1")]
    [Trait("Criterio", "CA-HU15.1")]
    public async Task ElDbaRegistraQdLabYElWorkerLaDejaActivaConSuPerfil()
    {
        var detail = (await platform.QdLabAsync()).Probed;

        Assert.True(detail.GetProperty("status").GetString() == "Active", $"Se esperaba Active: {detail}");
        var capabilities = detail.GetProperty("capabilities");
        var version = capabilities.GetProperty("engineVersion").GetString();
        output.WriteLine($"Plataforma bajo prueba: {version} — {capabilities.GetProperty("edition").GetString()} — {capabilities.GetProperty("supportLevel").GetString()}");

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Equal("Tested", capabilities.GetProperty("supportLevel").GetString());

        var queryStore = capabilities.GetProperty("queryStore");
        Assert.Equal("READ_WRITE", queryStore.GetProperty("actualState").GetString());
        Assert.Equal(1, queryStore.GetProperty("intervalLengthMinutes").GetInt32());
        Assert.Equal("ALL", queryStore.GetProperty("captureMode").GetString());
        Assert.Equal(30, queryStore.GetProperty("retentionDays").GetInt32());
        Assert.True(queryStore.GetProperty("canIngest").GetBoolean());
    }
}
