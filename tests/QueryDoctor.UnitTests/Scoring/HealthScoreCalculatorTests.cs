using QueryDoctor.Application.Scoring;
using QueryDoctor.Domain.Analysis;

namespace QueryDoctor.UnitTests.Scoring;

/// <summary>
/// Pruebas unitarias de <see cref="HealthScoreCalculator"/> y <see cref="HealthScoreParameters"/>
/// (S2-02, RF-SCO-01..06, CA-HU08.1..08.3, Ecuaciones 4.1–4.5 del SRS v1.1).
/// </summary>
public sealed class HealthScoreCalculatorTests
{
    private readonly HealthScoreCalculator _calculator = new();

    [Fact]
    public void EjemploCalculadoAManoProduceValoresExactosDeConsumoImpactoSeveridadPrioridadYScore()
    {
        // Ventana W con totales: CPU = 1_000_000 us, DUR = 2_000_000 us, LECT = 100_000 lecturas.
        var q1Id = new Guid("11111111-1111-1111-1111-111111111111");
        var q2Id = new Guid("22222222-2222-2222-2222-222222222222");
        var q3Id = new Guid("33333333-3333-3333-3333-333333333333");
        var qRestId = new Guid("44444444-4444-4444-4444-444444444444");

        // q1: C_CPU=60_000 (s=0.06), C_DUR=100_000 (s=0.05), C_LECT=5_000 (s=0.05) -> weighted=0.054 -> I=min(1, 0.054/0.05)=1.0
        // Hallazgos: REG-01 (Alta=0.8, Estimada=0.6 -> 0.48) y REG-04 (Media=0.5, Estimada=0.6 -> 0.30)
        // S = 1 - (0.52 * 0.70) = 0.636; P = 100 * 1.0 * (0.5 + 0.5 * 0.636) = 81.8; H = 18.2 (Crítica)
        var q1 = new QueryScoreInput(
            q1Id,
            101,
            [
                new QueryResourceSample(100, 400.0, 500.0, 25.0),
                new QueryResourceSample(100, 200.0, 500.0, 25.0),
            ],
            [
                new QueryFindingSample("REG-01", "F-000000000001", FindingSeverity.Alta, EvidenceClass.Estimada),
                new QueryFindingSample("REG-04", "F-000000000002", FindingSeverity.Media, EvidenceClass.Estimada),
            ]);

        // q2: s_CPU=0.04, s_DUR=0.04, s_LECT=0.04 -> weighted=0.04 -> I=0.04/0.05=0.80
        // Hallazgo: REG-03 (Media=0.5, Estimada=0.6 -> 0.30) -> S=0.30
        // P = 100 * 0.80 * (0.5 + 0.5 * 0.30) = 52.0; H = 48.0 (Atención)
        var q2 = new QueryScoreInput(
            q2Id,
            102,
            [new QueryResourceSample(100, 400.0, 800.0, 40.0)],
            [new QueryFindingSample("REG-03", "F-000000000003", FindingSeverity.Media, EvidenceClass.Estimada)]);

        // q3: s_CPU=0.01, s_DUR=0.01, s_LECT=0.01 -> weighted=0.01 -> I=0.20, S=0.0 -> P=10.0, H=90.0 (Saludable)
        var q3 = new QueryScoreInput(
            q3Id,
            103,
            [new QueryResourceSample(10, 1_000.0, 2_000.0, 100.0)],
            []);

        // qRest: completa el 100% de la ventana W para que los denominadores sean exactamente 1_000_000 / 2_000_000 / 100_000.
        var qRest = new QueryScoreInput(
            qRestId,
            104,
            [new QueryResourceSample(100, 8_900.0, 18_000.0, 900.0)],
            []);

        var results = _calculator.CalculateWindow([q3, q2, qRest, q1]);

        var r1 = results.Single(r => r.QueryId == q1Id);
        Assert.Equal(200L, r1.TotalExecutions);
        Assert.Equal(60_000.0, r1.TotalCpuMicroseconds, 6);
        Assert.Equal(100_000.0, r1.TotalDurationMicroseconds, 6);
        Assert.Equal(5_000.0, r1.TotalLogicalReads, 6);
        Assert.Equal(0.06, r1.CpuShare, 6);
        Assert.Equal(0.05, r1.DurationShare, 6);
        Assert.Equal(0.05, r1.LogicalReadsShare, 6);
        Assert.Equal(5.4, r1.WeightedConsumptionPct, 6);
        Assert.Equal(1.0, r1.ImpactComponent, 6);
        Assert.Equal(0.636, r1.SeverityComponent, 6);
        Assert.Equal(81.8, r1.Priority, 6);
        Assert.Equal(18.2, r1.Score, 6);
        Assert.Equal(HealthScoreBand.Critica, r1.Band);
        Assert.Equal("CRITICA", r1.BandCode);
        Assert.Equal(2, r1.FindingsCount);

        var r2 = results.Single(r => r.QueryId == q2Id);
        Assert.Equal(0.80, r2.ImpactComponent, 6);
        Assert.Equal(0.30, r2.SeverityComponent, 6);
        Assert.Equal(52.0, r2.Priority, 6);
        Assert.Equal(48.0, r2.Score, 6);
        Assert.Equal(HealthScoreBand.Atencion, r2.Band);
        Assert.Equal("ATENCION", r2.BandCode);

        var r3 = results.Single(r => r.QueryId == q3Id);
        Assert.Equal(0.20, r3.ImpactComponent, 6);
        Assert.Equal(0.0, r3.SeverityComponent, 6);
        Assert.Equal(10.0, r3.Priority, 6);
        Assert.Equal(90.0, r3.Score, 6);
        Assert.Equal(HealthScoreBand.Saludable, r3.Band);
        Assert.Equal("SALUDABLE", r3.BandCode);

        // El orden por defecto ScoreAscending coloca peor score primero (q1, luego qRest con 50.0 vs q2 con 48.0, etc.)
        Assert.Equal(q1Id, results[0].QueryId);
        Assert.Equal(q2Id, results[1].QueryId);
        Assert.Equal(HealthScoreParameters.ScaleDirection, r1.ScaleDirection);
        Assert.Equal(HealthScoreParameters.UsageDisclaimer, r1.UsageDisclaimer);
    }

    [Fact]
    public void MismosDatosYParametrosProducenMismoScoreSinImportarOrdenDeEntrada()
    {
        var q1Id = Guid.NewGuid();
        var q2Id = Guid.NewGuid();

        var sample1A = new QueryResourceSample(50, 200.0, 300.0, 10.0);
        var sample1B = new QueryResourceSample(25, 100.0, 150.0, 20.0);
        var finding1A = new QueryFindingSample("REG-01", "F-AAA111", FindingSeverity.Alta, EvidenceClass.Estimada);
        var finding1B = new QueryFindingSample("REG-02", "F-BBB222", FindingSeverity.Media, EvidenceClass.PlanReal);

        var q1OrderA = new QueryScoreInput(q1Id, 1, [sample1A, sample1B], [finding1A, finding1B]);
        var q2OrderA = new QueryScoreInput(q2Id, 2, [new QueryResourceSample(10, 100.0, 100.0, 5.0)], []);

        var q1OrderB = new QueryScoreInput(q1Id, 1, [sample1B, sample1A], [finding1B, finding1A]);
        var q2OrderB = new QueryScoreInput(q2Id, 2, [new QueryResourceSample(10, 100.0, 100.0, 5.0)], []);

        var runA = _calculator.CalculateWindow([q1OrderA, q2OrderA]);
        var runB = _calculator.CalculateWindow([q2OrderB, q1OrderB]);

        Assert.Equal(runA.Count, runB.Count);
        for (var i = 0; i < runA.Count; i++)
        {
            Assert.Equal(runA[i].QueryId, runB[i].QueryId);
            Assert.Equal(runA[i].Score, runB[i].Score, 12);
            Assert.Equal(runA[i].Priority, runB[i].Priority, 12);
            Assert.Equal(runA[i].Band, runB[i].Band);
            Assert.Equal(runA[i].ContributingFindingsJson, runB[i].ContributingFindingsJson);
            Assert.Equal(runA[i].ParametersJson, runB[i].ParametersJson);
            Assert.Equal(runA[i].ParametersHash, runB[i].ParametersHash);
        }
    }

    [Fact]
    public void ConsultaCostosaSinHallazgosRecibePrioridadBaseAlpha()
    {
        // Una consulta con el 100% del consumo (I=1.0) y 0 hallazgos (S=0.0), con alpha=0.5:
        // P = 100 * 1.0 * 0.5 = 50.0; H = 50.0 (Atención).
        var qId = Guid.NewGuid();
        var results = _calculator.CalculateWindow([
            new QueryScoreInput(qId, 1, [new QueryResourceSample(100, 1_000.0, 2_000.0, 50.0)], []),
        ]);

        var single = Assert.Single(results);
        Assert.Equal(1.0, single.ImpactComponent, 6);
        Assert.Equal(0.0, single.SeverityComponent, 6);
        Assert.Equal(50.0, single.Priority, 6);
        Assert.Equal(50.0, single.Score, 6);
        Assert.Equal(HealthScoreBand.Atencion, single.Band);
    }

    [Fact]
    public void HallazgosInformativosComoReg11NoPenalizanLaSeveridad()
    {
        var qId = Guid.NewGuid();
        var results = _calculator.CalculateWindow([
            new QueryScoreInput(
                qId,
                1,
                [new QueryResourceSample(10, 100.0, 100.0, 10.0)],
                [new QueryFindingSample("REG-11", "F-REG11", FindingSeverity.Informativa, EvidenceClass.NoAplica, FindingKind.PlanNoAnalizable)]),
        ]);

        var single = Assert.Single(results);
        Assert.Equal(0.0, single.SeverityComponent, 12);
        Assert.Equal(50.0, single.Score, 6);
    }

    [Fact]
    public void ConsumoCeroEnLaVentanaProduceScoreCienSaludable()
    {
        var qId = Guid.NewGuid();
        var results = _calculator.CalculateWindow([
            new QueryScoreInput(
                qId,
                1,
                [new QueryResourceSample(0, 500.0, 500.0, 10.0)],
                [new QueryFindingSample("REG-01", "F-1", FindingSeverity.Alta, EvidenceClass.Estimada)]),
        ]);

        var single = Assert.Single(results);
        Assert.Equal(0.0, single.ImpactComponent, 12);
        Assert.Equal(0.0, single.Priority, 12);
        Assert.Equal(100.0, single.Score, 12);
        Assert.Equal(HealthScoreBand.Saludable, single.Band);
    }

    [Theory]
    [InlineData(0.0, HealthScoreBand.Critica)]
    [InlineData(39.0, HealthScoreBand.Critica)]
    [InlineData(39.99, HealthScoreBand.Critica)]
    [InlineData(40.0, HealthScoreBand.Atencion)]
    [InlineData(69.0, HealthScoreBand.Atencion)]
    [InlineData(69.99, HealthScoreBand.Atencion)]
    [InlineData(70.0, HealthScoreBand.Saludable)]
    [InlineData(100.0, HealthScoreBand.Saludable)]
    public void FronterasDeBandasCumplenEspecificacionNormativa(double score, HealthScoreBand expectedBand)
    {
        var actual = HealthScoreParameters.Default.ResolveBand(score);
        Assert.Equal(expectedBand, actual);
    }

    [Fact]
    public void PermiteOrdenarPorScoreAscendenteOPorConsumoDescendente()
    {
        // qHeavy: consume 80% de la ventana pero tiene 0 hallazgos (I=1, S=0 -> P=50, H=50).
        // qToxic: consume 20% (> tau=5% => I=1) y tiene 2 hallazgos Alta PlanReal -> S=0.96 -> P=98, H=2.
        var qHeavyId = Guid.NewGuid();
        var qToxicId = Guid.NewGuid();

        var inputs = new[]
        {
            new QueryScoreInput(qHeavyId, 1, [new QueryResourceSample(100, 800.0, 800.0, 80.0)], []),
            new QueryScoreInput(
                qToxicId,
                2,
                [new QueryResourceSample(100, 200.0, 200.0, 20.0)],
                [
                    new QueryFindingSample("REG-01", "F-1", FindingSeverity.Alta, EvidenceClass.PlanReal),
                    new QueryFindingSample("REG-06", "F-2", FindingSeverity.Alta, EvidenceClass.PlanReal),
                ]),
        };

        var byScore = _calculator.CalculateWindow(inputs, orderBy: HealthScoreOrderBy.ScoreAscending);
        Assert.Equal([qToxicId, qHeavyId], byScore.Select(r => r.QueryId).ToArray());

        var byConsumption = _calculator.CalculateWindow(inputs, orderBy: HealthScoreOrderBy.ConsumptionDescending);
        Assert.Equal([qHeavyId, qToxicId], byConsumption.Select(r => r.QueryId).ToArray());
    }

    [Fact]
    public void PersisteParametrosYValidaRestriccionesDePesosYTau()
    {
        var custom = new HealthScoreParameters
        {
            CpuWeight = 0.5,
            DurationWeight = 0.25,
            LogicalReadsWeight = 0.25,
            SaturationCapTau = 0.10,
            BasePriorityAlpha = 0.3,
        };

        var results = _calculator.CalculateWindow(
            [new QueryScoreInput(Guid.NewGuid(), 1, [new QueryResourceSample(10, 100.0, 100.0, 10.0)], [])],
            parameters: custom);

        var single = Assert.Single(results);
        Assert.Equal(custom.ToCanonicalJson(), single.ParametersJson);
        Assert.Equal(custom.ComputeConfigurationHash(), single.ParametersHash);
        Assert.Equal(64, single.ParametersHash.Length);
        Assert.Equal(70.0, single.Score, 6);

        var invalidWeights = custom with { CpuWeight = 0.9 };
        Assert.Throws<InvalidOperationException>(() => invalidWeights.Validate());

        var invalidTau = custom with { SaturationCapTau = 0.0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => invalidTau.Validate());
    }
}
