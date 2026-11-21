using QueryDoctor.PerfTests;

namespace QueryDoctor.UnitTests.Perf;

/// <summary>S3-07: la lógica pura de la consola de medición (tests/QueryDoctor.PerfTests), sin red ni laboratorio.</summary>
public sealed class PerfToolTests
{
    [Fact]
    public void PerfArgsLeeComandoYOpciones()
    {
        var args = PerfArgs.Parse(Split("INGEST --secrets C:/s --connection-id 3f2504e0-4f89-11d3-9a0c-0305e82c3301 --timeout-seconds 60"));

        Assert.Equal("ingest", args.Command);
        Assert.Equal("C:/s", args.Required("secrets"));
        Assert.Equal(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301"), args.RequiredGuid("connection-id"));
        Assert.Equal(60, args.OptionalInt("timeout-seconds", 1800, 10, 7200));
        Assert.Equal("http://x", args.Optional("base-url", "http://x"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("--secrets x")]
    [InlineData("setup --secrets")]
    [InlineData("setup --secrets --out")]
    [InlineData("setup secrets x")]
    [InlineData("setup --out a --out b")]
    public void PerfArgsRechazaArgumentosMalFormados(string raw) =>
        Assert.Throws<ArgumentException>(() => PerfArgs.Parse(Split(raw)));

    [Fact]
    public void PerfArgsRechazaEnterosFueraDeRangoYOpcionesFaltantes()
    {
        var args = PerfArgs.Parse(Split("ingest --timeout-seconds 5"));

        Assert.Throws<ArgumentException>(() => args.OptionalInt("timeout-seconds", 1800, 10, 7200));
        Assert.Throws<ArgumentException>(() => args.Required("secrets"));
    }

    [Fact]
    public void SessionCookiesReenviaSoloNombreYValor()
    {
        var header = SessionCookies.ToCookieHeader(
        [
            "qd.session=abc123; path=/; secure; samesite=strict; httponly",
            ".AspNetCore.Antiforgery=xyz; path=/",
        ]);

        Assert.Equal("qd.session=abc123; .AspNetCore.Antiforgery=xyz", header);
    }

    [Fact]
    public void SessionCookiesLaUltimaGanaYUnaVaciadaSeDescarta()
    {
        Assert.Equal("a=2", SessionCookies.ToCookieHeader(["a=1; path=/", "a=2; path=/"]));
        Assert.Null(SessionCookies.ToCookieHeader(["qd.session=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/"]));
        Assert.Null(SessionCookies.ToCookieHeader([]));
    }

    [Fact]
    public void FindManualRunIgnoraLasViejasYLasProgramadas()
    {
        var old = Run("Manual", "Succeeded", 1);
        var scheduled = Run("Scheduled", "Succeeded", 3);
        var mine = Run("Manual", "Running", 2);
        var before = new HashSet<Guid> { old.Id };

        var found = IngestionCommands.FindManualRun([scheduled, mine, old], before);

        Assert.Equal(mine.Id, found?.Id);
        Assert.Null(IngestionCommands.FindManualRun([old, scheduled], before));
    }

    private static string[] Split(string raw) => raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static IngestionRunDto Run(string trigger, string status, int minute) =>
        new(Guid.NewGuid(), trigger, status, new DateTimeOffset(2026, 11, 20, 12, minute, 0, TimeSpan.Zero), null, null, null);
}
