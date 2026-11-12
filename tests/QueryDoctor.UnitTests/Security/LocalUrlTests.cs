using QueryDoctor.Application.Common.Security;

namespace QueryDoctor.UnitTests.Security;

public class LocalUrlTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/conexiones")]
    [InlineData("/conexiones/3?tab=estado")]
    public void AceptaRutasLocales(string url)
    {
        Assert.Equal(url, LocalUrl.Sanitize(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://evil.example/")]
    [InlineData("http://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example/")]
    [InlineData("\\\\evil.example")]
    [InlineData("conexiones")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/ok\r\nSet-Cookie: x=1")]
    [InlineData("/ok\tmal")]
    public void RechazaRutasNoLocales(string? url)
    {
        Assert.Equal("/", LocalUrl.Sanitize(url));
    }

    [Fact]
    public void RechazaUrlDemasiadoLarga()
    {
        var largo = "/" + new string('a', 3000);

        Assert.Equal("/", LocalUrl.Sanitize(largo));
    }

    [Fact]
    public void UsaElValorDeReservaIndicado()
    {
        Assert.Equal("/inicio", LocalUrl.Sanitize("//evil.example", "/inicio"));
    }
}
