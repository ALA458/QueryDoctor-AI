using QueryDoctor.Application.Ingestion;
using QueryDoctor.Domain.Core;
using static QueryDoctor.UnitTests.Ingestion.IngestionSamples;

namespace QueryDoctor.UnitTests.Ingestion;

// RF-ING-04: agregación ponderada por número de ejecuciones, nunca promedio de promedios.
public class RuntimeStatAggregatorTests
{
    // (ejecuciones, promedio) de cada fila → promedio ponderado esperado.
    public static TheoryData<long[], double[], double> WeightedCases => new()
    {
        // Una sola fila: el mismo valor.
        { [5], [100d], 100d },
        // 1 ejecución de 10 y 3 de 30: (10 + 90) / 4 = 25. El promedio simple daría 20.
        { [1, 3], [10d, 30d], 25d },
        // 9 de 100 y 1 de 1000: 1900 / 10 = 190. El promedio simple daría 550.
        { [9, 1], [100d, 1000d], 190d },
        // Una fila sin ejecuciones no pesa: (0·999 + 2·50) / 2 = 50.
        { [0, 2], [999d, 50d], 50d },
        // Ninguna ejecución: no hubo consumo, 0 (no se divide por cero).
        { [0, 0], [10d, 20d], 0d },
        // Tres filas en el intervalo activo.
        { [2, 2, 4], [1d, 3d, 6d], 4d },
    };

    [Theory]
    [MemberData(nameof(WeightedCases))]
    public void ElPromedioSePonderaPorEjecuciones(long[] counts, double[] averages, double expected)
    {
        var parts = counts.Select((c, i) => Metrics(c, averages[i])).ToList();

        Assert.Equal(expected, RuntimeStatAggregator.WeightedAverage(parts, m => m.AvgDurationMicroseconds), 9);
    }

    // Columnas opcionales (TempDB, bytes de log): null = la versión no las expone.
    public static TheoryData<long[], double?[], double?> OptionalCases => new()
    {
        { [3, 1], [null, null], null },
        { [3, 1], [null, 8d], 8d },
        { [1, 3], [10d, 30d], 25d },
        { [0, 0], [5d, null], 0d },
        { [2, 0], [null, 7d], 0d },
    };

    [Theory]
    [MemberData(nameof(OptionalCases))]
    public void LasColumnasOpcionalesSoloPonderanFilasConValor(long[] counts, double?[] values, double? expected)
    {
        var parts = counts.Select((c, i) => Metrics(c, 1d, values[i])).ToList();

        var actual = RuntimeStatAggregator.WeightedAverage(parts, m => m.AvgTempDbSpacePages);

        if (expected is null)
        {
            Assert.Null(actual);
        }
        else
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Value, actual.Value, 9);
        }
    }

    [Fact]
    public void LasEjecucionesNegativasSonUnErrorDeDatos()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RuntimeStatAggregator.WeightedAverage([Metrics(-1, 1d)], m => m.AvgDop));
    }

    [Fact]
    public void AgregaPorPlanTipoEIntervaloYSumaEjecuciones()
    {
        var rows = new[]
        {
            Row(7, 2, Metrics(1, 10d)),
            Row(7, 2, Metrics(3, 30d)),
            Row(7, 2, Metrics(5, 50d), type: ExecutionType.Aborted),
            Row(7, 3, Metrics(2, 40d)),
            Row(8, 2, Metrics(4, 80d)),
        };

        var result = RuntimeStatAggregator.Aggregate(rows);

        Assert.Equal(4, result.Count);
        var merged = Assert.Single(result, r => r.QueryStorePlanId == 7 && r.IntervalId == 2 && r.ExecutionType == ExecutionType.Regular);
        Assert.Equal(4, merged.Metrics.ExecutionCount);
        Assert.Equal(25d, merged.Metrics.AvgDurationMicroseconds, 9);
        Assert.Equal(25d, merged.Metrics.AvgCpuTimeMicroseconds, 9);
        Assert.Equal(25d, merged.Metrics.AvgMaxUsedMemoryPages, 9);
    }

    [Fact]
    public void LaSalidaEsDeterministaSinImportarElOrdenDeEntrada()
    {
        var rows = new[]
        {
            Row(9, 5, Metrics(1, 1d)),
            Row(1, 5, Metrics(1, 2d)),
            Row(1, 4, Metrics(1, 3d), type: ExecutionType.Exception),
            Row(1, 4, Metrics(1, 4d)),
        };

        var forward = RuntimeStatAggregator.Aggregate(rows);
        var backward = RuntimeStatAggregator.Aggregate(rows.Reverse());

        Assert.Equal(forward, backward);
        Assert.Equal(
            [(4L, 1L, ExecutionType.Regular), (4L, 1L, ExecutionType.Exception), (5L, 1L, ExecutionType.Regular), (5L, 9L, ExecutionType.Regular)],
            forward.Select(r => (r.IntervalId, r.QueryStorePlanId, r.ExecutionType)).ToList());
    }

    [Fact]
    public void UnGrupoConAlgunaFilaAbiertaQuedaAbierto()
    {
        var result = RuntimeStatAggregator.Aggregate([Row(1, 2, Metrics(1, 1d), closed: true), Row(1, 2, Metrics(1, 1d), closed: false)]);

        Assert.False(Assert.Single(result).IntervalClosed);
    }
}
