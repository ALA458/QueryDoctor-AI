using System.Text.Json;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.LabTests;

namespace QueryDoctor.AcceptanceTests;

/// <summary>
/// HU-03 a HU-08, HU-13 y HU-15 de punta a punta con el DBA: la ingesta manual por la Api trae la carga sembrada de
/// QD_Lab (S1-07), el Worker la analiza y puntúa, y todo se lee por la Api como lo lee la SPA. Los escenarios se
/// reconocen por su texto (enmascarado y sin comentarios) y por la regla que disparan, nunca entrando a la base.
/// </summary>
[Collection(AcceptanceGroup.Name)]
[Trait("Suite", "Aceptacion")]
public sealed class AnalysisAcceptanceTests(AcceptanceFixture platform)
{
    private static readonly string[] EvidenceClasses = ["ESTIMADA", "EJECUCION_AGREGADA", "PLAN_REAL"];

    // CA-HU03.1: textos, planes y métricas por intervalo almacenados, y la fecha de la última ingesta visible.
    [LabFact]
    [Trait("Criterio", "CA-HU03.1")]
    public async Task LaIngestaManualGuardaTextosPlanesYMetricasYMuestraLaUltimaIngesta()
    {
        var lab = await platform.QdLabAsync();

        Assert.True(lab.FirstIngestion.GetProperty("status").GetString() == "Succeeded", $"Ingesta: {lab.FirstIngestion}");
        Assert.Equal("Manual", lab.FirstIngestion.GetProperty("trigger").GetString());
        Assert.True(lab.FirstIngestion.GetProperty("rowsUpserted").GetInt32() > 0);

        var connection = await Platform.GetJsonAsync(lab.Dba, $"/connections/{lab.Id}");
        Assert.Equal(JsonValueKind.String, connection.GetProperty("lastIngestionAt").ValueKind);

        var detail = await DetailAsync(lab, await Esc01Async(lab));
        Assert.False(string.IsNullOrWhiteSpace(detail.GetProperty("sqlText").GetString()));
        Assert.NotEqual(0, detail.GetProperty("plans").GetArrayLength());
        Assert.NotEqual(0, detail.GetProperty("series").GetArrayLength());
    }

    // CA-HU03.2: una segunda ingesta seguida no duplica consultas, planes ni puntos de las series.
    [LabFact]
    [Trait("Criterio", "CA-HU03.2")]
    public async Task UnaSegundaIngestaNoDuplicaRegistros()
    {
        var lab = await platform.QdLabAsync();

        var second = await Platform.IngestAsync(lab.Dba, lab.Id);

        Assert.True(second.GetProperty("status").GetString() == "Succeeded", $"Segunda ingesta: {second}");
        var queries = await QueriesAsync(lab, "window=24h");
        var queryStoreIds = queries.EnumerateArray().Select(q => q.GetProperty("queryStoreQueryId").GetInt64()).ToList();
        Assert.Equal(queryStoreIds.Count, queryStoreIds.Distinct().Count());

        foreach (var query in queries.EnumerateArray())
        {
            var detail = await DetailAsync(lab, query.GetProperty("id").GetGuid());
            var plans = detail.GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("queryStorePlanId").GetInt64()).ToList();
            Assert.Equal(plans.Count, plans.Distinct().Count());
            var points = detail.GetProperty("series").EnumerateArray()
                .Select(s => (s.GetProperty("intervalId").GetInt64(), s.GetProperty("planId").GetGuid()))
                .ToList();
            Assert.Equal(points.Count, points.Distinct().Count());
        }
    }

    // CA-HU02.3 / RNF-SEG-01: después de sondear, ingerir y analizar, la configuración de Query Store es la misma.
    [LabFact]
    [Trait("Criterio", "CA-HU02.3")]
    public async Task ElFlujoCompletoNoModificaLaConfiguracionDeQueryStore()
    {
        var lab = await platform.QdLabAsync();

        Assert.NotEmpty(lab.QueryStoreBefore);
        Assert.Equal(lab.QueryStoreBefore, await LabSql.QueryStoreConfigurationAsync(LabEnvironment.Database));
    }

    // CA-HU15.2 / RF-TRZ-04: el Worker solo emitió SELECT constantes de la lista blanca. CA-HU06.2: ninguna sugerencia
    // de índice se ejecuta (no hay CREATE INDEX ni otra cosa que no sea SELECT).
    [LabFact]
    [Trait("Criterio", "CA-HU15.2")]
    [Trait("Criterio", "CA-HU06.2")]
    public async Task ElWorkerSoloEmitioSelectDeLaListaBlanca()
    {
        await platform.QdLabAsync();

        var entries = platform.StatementLog.GetEntries();
        Assert.NotEmpty(entries);
        Assert.All(entries, entry =>
        {
            TelemetryStatementWhitelist.ValidateStatementOrThrow(entry.StatementKey, entry.StatementText);
            Assert.Equal(TelemetryStatementWhitelist.GetSql(entry.StatementKey), entry.StatementText);
            Assert.DoesNotContain("CREATE INDEX", entry.StatementText, StringComparison.OrdinalIgnoreCase);
        });
    }

    // CA-HU05.1: la conversión implícita (ESC01) se ve con el tipo de problema, la expresión afectada y el nodo.
    [LabFact]
    [Trait("Criterio", "CA-HU05.1")]
    public async Task LaConversionImplicitaSembradaApareceConExpresionYNodo()
    {
        var lab = await platform.QdLabAsync();

        var finding = Finding(await DetailAsync(lab, await Esc01Async(lab)), "REG-01");
        var evidence = finding.GetProperty("evidence");

        Assert.False(string.IsNullOrWhiteSpace(evidence.GetProperty("convert_issue").GetString()));
        Assert.Contains("CONVERT_IMPLICIT", evidence.GetProperty("expression").GetString(), StringComparison.Ordinal);
        Assert.NotEqual(0, finding.GetProperty("nodes").GetArrayLength());
    }

    // CA-HU05.2: un Key Lookup por encima del umbral muestra el número estimado de ejecuciones y el umbral aplicado.
    // Es ACC01 (lab/sql/12): ESC02 estima ~20 lookups y queda por debajo del umbral por defecto de REG-04 (1000).
    [LabFact]
    [Trait("Criterio", "CA-HU05.2")]
    public async Task ElKeyLookupPorEncimaDelUmbralMuestraEjecucionesEstimadasYUmbral()
    {
        var lab = await platform.QdLabAsync();

        var finding = Finding(await DetailAsync(lab, await QueryWithRuleAsync(lab, "REG-04", "CustomerId BETWEEN")), "REG-04");
        var evidence = finding.GetProperty("evidence");

        var estimated = double.Parse(evidence.GetProperty("estimated_executions").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var threshold = double.Parse(evidence.GetProperty("min_estimated_executions_threshold").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(estimated >= threshold, $"{estimated} ejecuciones estimadas con umbral {threshold}.");
    }

    // CA-HU06.1 y CA-HU06.3: el índice faltante (ESC03) trae tabla, columnas, impacto y la advertencia de escritura.
    [LabFact]
    [Trait("Criterio", "CA-HU06.1")]
    [Trait("Criterio", "CA-HU06.3")]
    public async Task ElIndiceFaltanteSembradoTraeColumnasImpactoYAdvertenciaDeEscritura()
    {
        var lab = await platform.QdLabAsync();

        var finding = Finding(await DetailAsync(lab, await QueryWithRuleAsync(lab, "REG-02", "Region")), "REG-02");
        var evidence = finding.GetProperty("evidence");

        Assert.Contains("Orders", evidence.GetProperty("table").GetString(), StringComparison.Ordinal);
        Assert.Contains("Region", evidence.GetProperty("equality_columns").GetString(), StringComparison.Ordinal);
        Assert.Contains("Amount", evidence.GetProperty("inequality_columns").GetString(), StringComparison.Ordinal);
        Assert.True(evidence.TryGetProperty("include_columns", out _));
        Assert.False(string.IsNullOrWhiteSpace(evidence.GetProperty("impact_pct").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(evidence.GetProperty("write_cost_warning").GetString()));
    }

    // CA-HU05.4: todo hallazgo indica su clase de evidencia.
    [LabFact]
    [Trait("Criterio", "CA-HU05.4")]
    public async Task TodoHallazgoIndicaSuClaseDeEvidencia()
    {
        var lab = await platform.QdLabAsync();

        var findings = 0;
        foreach (var query in (await QueriesAsync(lab, "window=24h")).EnumerateArray().Where(q => q.GetProperty("findings").GetArrayLength() > 0))
        {
            foreach (var finding in (await DetailAsync(lab, query.GetProperty("id").GetGuid())).GetProperty("findings").EnumerateArray())
            {
                Assert.Contains(finding.GetProperty("evidenceClass").GetString(), EvidenceClasses);
                findings++;
            }
        }

        Assert.True(findings >= 3, $"Se esperaban al menos los hallazgos de ESC01, ESC02 y ESC03; hubo {findings}.");
    }

    // CA-HU04.1 y CA-HU04.3: orden por score o por porcentaje de consumo, con filtros por base, ventana y severidad.
    [LabFact]
    [Trait("Criterio", "CA-HU04.1")]
    [Trait("Criterio", "CA-HU04.3")]
    public async Task ElListadoOrdenaPorConsumoOScoreYFiltraPorConexionVentanaYSeveridad()
    {
        var lab = await platform.QdLabAsync();
        var esc01 = await Esc01Async(lab);

        var byConsumption = (await QueriesAsync(lab, "window=24h&sort=consumption")).EnumerateArray().ToList();
        Assert.NotEmpty(byConsumption);
        Assert.All(byConsumption, q => Assert.Equal(lab.Id, q.GetProperty("connectionId").GetGuid()));
        Assert.All(byConsumption, q => Assert.Equal(LabEnvironment.Database, q.GetProperty("databaseName").GetString()));
        var shares = byConsumption.Select(q => q.GetProperty("consumptionPct").GetDouble()).ToList();
        Assert.Equal(shares.OrderByDescending(s => s), shares);
        Assert.All(shares, s => Assert.InRange(s, 0, 100));
        Assert.InRange(shares.Sum(), 99.0, 101.0); // participación de cada consulta en el total de la ventana

        var scores = (await QueriesAsync(lab, "window=24h&sort=score")).EnumerateArray()
            .Select(q => q.GetProperty("score"))
            .Where(s => s.ValueKind == JsonValueKind.Number)
            .Select(s => s.GetDouble())
            .ToList();
        Assert.NotEmpty(scores);
        Assert.Equal(scores.Order(), scores);

        // El filtro recibe High|Medium|Low|Info y la respuesta muestra la severidad en español (contrato de S1-16).
        var high = (await QueriesAsync(lab, "window=7d&severity=High")).EnumerateArray().ToList();
        Assert.NotEmpty(high);
        Assert.Contains(high, q => q.GetProperty("id").GetGuid() == esc01);
        Assert.All(high, q => Assert.Contains(q.GetProperty("findings").EnumerateArray(), f => f.GetProperty("severity").GetString() is "Alta" or "High"));
        var all = (await QueriesAsync(lab, "window=7d")).GetArrayLength();
        Assert.True(high.Count < all, "El filtro por severidad no dejó fuera ninguna consulta.");
    }

    // CA-HU07.1: con al menos dos intervalos hay series de duración, CPU y lecturas. CA-HU07.2: las series distinguen
    // los planes de una consulta que cambió de plan (ESC04 en el laboratorio).
    [LabFact]
    [Trait("Criterio", "CA-HU07.1")]
    [Trait("Criterio", "CA-HU07.2")]
    public async Task LasSeriesTienenVariosIntervalosYDistinguenPlanes()
    {
        var lab = await platform.QdLabAsync();

        var sawTwoIntervals = false;
        var sawTwoPlans = false;
        foreach (var query in (await QueriesAsync(lab, "window=24h")).EnumerateArray())
        {
            var detail = await DetailAsync(lab, query.GetProperty("id").GetGuid());
            var series = detail.GetProperty("series").EnumerateArray().ToList();
            Assert.All(series, point =>
            {
                Assert.True(point.GetProperty("avgDurationMs").GetDouble() >= 0);
                Assert.True(point.GetProperty("avgCpuTimeMs").GetDouble() >= 0);
                Assert.True(point.GetProperty("avgLogicalIoReads").GetDouble() >= 0);
            });

            sawTwoIntervals |= series.Select(p => p.GetProperty("intervalId").GetInt64()).Distinct().Count() >= 2;
            sawTwoPlans |= detail.GetProperty("plans").GetArrayLength() >= 2
                && series.Select(p => p.GetProperty("planId").GetGuid()).Distinct().Count() >= 2;
        }

        Assert.True(sawTwoIntervals, "Ninguna consulta tiene dos intervalos: corre la carga del laboratorio dos veces con más de un minuto entre ellas.");
        Assert.True(sawTwoPlans, "Ninguna consulta con dos planes tiene series de ambos.");
    }

    // CA-HU08.1: score 0–100 con el desglose de sus componentes y pesos. CA-HU08.3: posición relativa en la ventana y
    // dirección de la escala.
    [LabFact]
    [Trait("Criterio", "CA-HU08.1")]
    [Trait("Criterio", "CA-HU08.3")]
    public async Task LaConsultaAnalizadaTieneScoreConDesglosePosicionYDireccion()
    {
        var lab = await platform.QdLabAsync();

        var score = (await DetailAsync(lab, await Esc01Async(lab))).GetProperty("score");

        Assert.InRange(score.GetProperty("score").GetDouble(), 0, 100);
        Assert.InRange(score.GetProperty("impactComponent").GetDouble(), 0, 100);
        Assert.InRange(score.GetProperty("severityComponent").GetDouble(), 0, 100);
        Assert.InRange(score.GetProperty("alphaUsed").GetDouble(), 0, 1);
        Assert.True(score.TryGetProperty("parametersUsed", out _));
        Assert.NotEqual(0, score.GetProperty("contributingFindings").GetArrayLength());

        var total = score.GetProperty("totalQueriesInWindow").GetInt32();
        Assert.InRange(score.GetProperty("rankInWindow").GetInt32(), 1, total);
        Assert.False(string.IsNullOrWhiteSpace(score.GetProperty("scaleDirection").GetString()));
    }

    // CA-HU13.1: el plan ingerido se ve en jerarquía con costo estimado y advertencias. CA-HU13.2: los nodos con
    // hallazgos vienen marcados.
    [LabFact]
    [Trait("Criterio", "CA-HU13.1")]
    [Trait("Criterio", "CA-HU13.2")]
    public async Task ElPlanSeVeEnJerarquiaConCostosAdvertenciasYNodosResaltados()
    {
        var lab = await platform.QdLabAsync();
        var queryId = await Esc01Async(lab);
        var planId = Finding(await DetailAsync(lab, queryId), "REG-01").GetProperty("planId").GetGuid();

        var tree = await Platform.GetJsonAsync(lab.Dba, $"/queries/{queryId}/plans/{planId}");

        Assert.False(tree.GetProperty("planExpired").GetBoolean());
        Assert.True(tree.GetProperty("highlightedOperatorsCount").GetInt32() > 0);
        var statement = Assert.Single(tree.GetProperty("statements").EnumerateArray(), s => s.GetProperty("operators").GetArrayLength() > 0);
        var operators = statement.GetProperty("operators").EnumerateArray().ToList();
        Assert.Contains(operators, o => o.GetProperty("depth").GetInt32() > 0);
        Assert.All(operators, o => Assert.True(o.GetProperty("estimatedTotalSubtreeCost").GetDouble() >= 0));
        Assert.True(statement.GetProperty("warnings").GetArrayLength() > 0
            || operators.Any(o => o.GetProperty("warnings").GetArrayLength() > 0));
        Assert.Contains(operators, o => o.GetProperty("hasFindings").GetBoolean());
    }

    internal static async Task<JsonElement> QueriesAsync(LabConnection lab, string query) =>
        await Platform.GetJsonAsync(lab.Dba, $"/queries?connectionId={lab.Id}&{query}");

    internal static Task<JsonElement> DetailAsync(LabConnection lab, Guid queryId) =>
        Platform.GetJsonAsync(lab.Dba, $"/queries/{queryId}?window=24h");

    /// <summary>ESC01: la conversión implícita sobre CustomerCode.</summary>
    internal static Task<Guid> Esc01Async(LabConnection lab) => QueryWithRuleAsync(lab, "REG-01", "CustomerCode");

    /// <summary>La consulta del escenario: su texto menciona la columna y su fila trae el hallazgo de la regla.</summary>
    internal static async Task<Guid> QueryWithRuleAsync(LabConnection lab, string ruleId, string column)
    {
        var queries = await QueriesAsync(lab, "window=24h");
        var match = queries.EnumerateArray().FirstOrDefault(q =>
            q.GetProperty("sqlPreview").GetString()!.Contains(column, StringComparison.Ordinal)
            && q.GetProperty("findings").EnumerateArray().Any(f => f.GetProperty("ruleId").GetString() == ruleId));

        Assert.True(match.ValueKind == JsonValueKind.Object, $"No hay consulta con {column} y {ruleId} en QD_Lab: {queries}");
        return match.GetProperty("id").GetGuid();
    }

    internal static JsonElement Finding(JsonElement detail, string ruleId)
    {
        var finding = detail.GetProperty("findings").EnumerateArray().FirstOrDefault(f => f.GetProperty("ruleId").GetString() == ruleId);
        Assert.True(finding.ValueKind == JsonValueKind.Object, $"El detalle no trae {ruleId}: {detail.GetProperty("findings")}");
        return finding;
    }
}
