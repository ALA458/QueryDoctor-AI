using QueryDoctor.Application.Common.Security;

namespace QueryDoctor.UnitTests.Security;

public class OriginCheckTests
{
    [Theory]
    [InlineData(null, "localhost")]
    [InlineData("", "localhost")]
    [InlineData("http://localhost:8080", "localhost")]
    [InlineData("https://LOCALHOST", "localhost")]
    public void PermiteOrigenPropioOAusente(string? origin, string host)
    {
        Assert.True(OriginCheck.IsAllowed(origin, host));
    }

    [Theory]
    [InlineData("https://evil.example", "localhost")]
    [InlineData("http://localhost.evil.example", "localhost")]
    [InlineData("null", "localhost")]
    [InlineData("no-es-una-url", "localhost")]
    public void RechazaOrigenAjeno(string origin, string host)
    {
        Assert.False(OriginCheck.IsAllowed(origin, host));
    }

    [Theory]
    [InlineData("same-origin")]
    [InlineData("none")]
    public void ConfiaEnSecFetchSiteDelMismoOrigen(string site)
    {
        // Con Referrer-Policy: no-referrer el navegador manda "Origin: null" en formularios del propio sitio.
        Assert.True(OriginCheck.IsAllowed("null", "localhost", site));
    }

    [Theory]
    [InlineData("cross-site")]
    [InlineData("same-site")]
    public void RechazaSecFetchSiteAjeno(string site)
    {
        Assert.False(OriginCheck.IsAllowed("http://localhost", "localhost", site));
    }
}
