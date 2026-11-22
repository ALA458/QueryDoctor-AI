using QueryDoctor.PerfTests;

namespace QueryDoctor.UnitTests.Perf;

/// <summary>S3-07 (RNF-RND-02): percentiles por rango más cercano, veredicto contra la meta y muestra con semilla.</summary>
public sealed class LatencyStatsTests
{
    private static readonly double[] OneToHundred = Enumerable.Range(1, 100).Select(i => (double)i).ToArray();

    [Theory]
    [InlineData(50, 50)]
    [InlineData(95, 95)]
    [InlineData(99, 99)]
    [InlineData(100, 100)]
    [InlineData(0.5, 1)]
    public void PercentilDeUnoACienEsElRangoMasCercano(double percentile, double expected) =>
        Assert.Equal(expected, LatencyStats.Percentile(OneToHundred, percentile));

    [Fact]
    public void PercentilConPocasMuestrasRedondeaHaciaArribaSinInterpolar()
    {
        double[] sorted = [10, 20, 30, 40, 50];

        // ⌈0,95 · 5⌉ = 5 y ⌈0,5 · 5⌉ = 3: siempre una latencia que de verdad ocurrió.
        Assert.Equal(50, LatencyStats.Percentile(sorted, 95));
        Assert.Equal(30, LatencyStats.Percentile(sorted, 50));
    }

    [Fact]
    public void PercentilRechazaSerieVaciaYPercentilFueraDeRango()
    {
        Assert.Throws<ArgumentException>(() => LatencyStats.Percentile([], 95));
        Assert.Throws<ArgumentOutOfRangeException>(() => LatencyStats.Percentile(OneToHundred, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LatencyStats.Percentile(OneToHundred, 101));
    }

    [Fact]
    public void ResumenOrdenaYCalculaMediaYDesviacionMuestral()
    {
        var summary = LatencyStats.Summarize([4, 2, 8, 6]);

        Assert.Equal(4, summary.Count);
        Assert.Equal(2, summary.MinMs);
        Assert.Equal(8, summary.MaxMs);
        Assert.Equal(5, summary.MeanMs);
        Assert.Equal(Math.Sqrt(20.0 / 3), summary.StdDevMs, 10);
        Assert.Equal(4, summary.P50Ms);
        Assert.Equal(8, summary.P95Ms);
    }

    [Fact]
    public void ResumenVacioNoInventaValores()
    {
        var summary = LatencyStats.Summarize([]);

        Assert.Equal(0, summary.Count);
        Assert.True(double.IsNaN(summary.P95Ms));
    }

    [Fact]
    public void VeredictoCumpleSoloSinErroresYConP95DentroDeLaMeta()
    {
        var bajo = LatencyStats.Summarize(OneToHundred.Select(x => x * 10));   // p95 = 950 ms
        var alto = LatencyStats.Summarize(OneToHundred.Select(x => x * 30));   // p95 = 2 850 ms

        Assert.True(DashboardCommands.Verdict(2000, 0, bajo));
        Assert.False(DashboardCommands.Verdict(2000, 0, alto));
        Assert.False(DashboardCommands.Verdict(2000, 1, bajo));
        Assert.False(DashboardCommands.Verdict(2000, 0, LatencyStats.Summarize([])));
        Assert.Null(DashboardCommands.Verdict(null, 0, bajo));
    }

    [Fact]
    public void LaMuestraConSemillaEsReproducibleYSinRepetidos()
    {
        var items = Enumerable.Range(0, 5000).ToArray();

        var a = DashboardCommands.SampleWithSeed(items, 100, 3007);
        var b = DashboardCommands.SampleWithSeed(items, 100, 3007);
        var c = DashboardCommands.SampleWithSeed(items, 100, 42);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(100, a.Distinct().Count());
        Assert.Equal(3, DashboardCommands.SampleWithSeed([1, 2, 3], 100, 1).Count);
    }

    [Fact]
    public void ListaDeConcurrenciasSeLeeYValida()
    {
        var args = PerfArgs.Parse(["dashboard", "--concurrency", "1, 5,10"]);

        Assert.Equal([1, 5, 10], args.OptionalIntList("concurrency", [1], 1, 64));
        Assert.Equal([1, 5], PerfArgs.Parse(["dashboard"]).OptionalIntList("concurrency", [1, 5], 1, 64));
        Assert.Throws<ArgumentException>(() => PerfArgs.Parse(["dashboard", "--concurrency", "1,1"]).OptionalIntList("concurrency", [1], 1, 64));
        Assert.Throws<ArgumentException>(() => PerfArgs.Parse(["dashboard", "--concurrency", "0"]).OptionalIntList("concurrency", [1], 1, 64));
    }
}
