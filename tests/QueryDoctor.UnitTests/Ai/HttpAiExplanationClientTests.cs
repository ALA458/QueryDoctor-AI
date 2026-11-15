using System.Net;
using System.Text;
using QueryDoctor.Application.Ai;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Infrastructure.Ai;
using QueryDoctor.UnitTests.Masking;

namespace QueryDoctor.UnitTests.Ai;

/// <summary>
/// Cliente HTTP hacia el servicio de IA (S2-05): un solo intento con tiempo total (E1, RNF-FIA-02), causas tipadas
/// para la degradación (RF-IA-06) y suite de literales sembrados sobre el cuerpo HTTP real (deuda de S2-04).
/// </summary>
public sealed class HttpAiExplanationClientTests
{
    private const string BaseUrl = "http://ai:8000";

    private static MaskedExplanationRequest Request()
    {
        var source = ExplanationContextBuilderTests.Source(out var findings);
        var kb = findings.Select(f => f.Rule.Id).Distinct().Select(r => ExplanationContextBuilderTests.Fragment(r)).ToArray();
        return new ExplanationContextBuilder(new Application.Masking.LiteralMasker(), ExplainQueryHarness.Catalog)
            .Build(source, kb, Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")).Request!;
    }

    private static HttpAiExplanationClient Client(StubHandler handler, int timeoutSeconds = 30, string? baseUrl = BaseUrl, int maxBytes = AiServiceOptions.DefaultMaxResponseBytes) =>
        new(new HttpClient(handler), new AiServiceOptions { BaseUrl = baseUrl, TimeoutSeconds = timeoutSeconds, MaxResponseBytes = maxBytes });

    [Fact]
    public async Task EnviaUnPostAExplainConElCuerpoEnmascaradoYLaCorrelacion()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") });

        var response = await Client(handler).SendAsync(Request(), CancellationToken.None);

        Assert.Equal("{\"ok\":true}", response);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(new Uri("http://ai:8000/explain"), sent.Uri);
        Assert.Equal("application/json", sent.ContentType);
        Assert.Equal("aaaaaaaa-0000-0000-0000-000000000001", sent.CorrelationId);
    }

    [Fact]
    public async Task ElCuerpoHttpRealNoLlevaNingunCentinela()
    {
        // CA-HU21.1 sobre lo que de verdad sale por la red, no sobre el doble de S2-04.
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });

        await Client(handler).SendAsync(Request(), CancellationToken.None);

        var body = Assert.Single(handler.Requests).Body;
        MaskingSentinels.AssertNoneIn(body);
        Assert.Contains("@qd_", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, ExplanationCause.InvalidOutput)]
    [InlineData(HttpStatusCode.BadGateway, ExplanationCause.ProviderError)]
    [InlineData(HttpStatusCode.InternalServerError, ExplanationCause.ProviderError)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ExplanationCause.ProviderError)]
    [InlineData(HttpStatusCode.GatewayTimeout, ExplanationCause.Timeout)]
    [InlineData(HttpStatusCode.BadRequest, ExplanationCause.Unexpected)]
    public async Task CadaFalloHttpSeTraduceAUnaCausaSinElCuerpoDeLaRespuesta(HttpStatusCode status, ExplanationCause expected)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent($"{{\"detail\":\"{MaskingSentinels.Secret}\"}}"),
        });

        var ex = await Assert.ThrowsAsync<AiServiceUnavailableException>(() => Client(handler).SendAsync(Request(), CancellationToken.None));

        Assert.Equal(expected, ex.Cause);
        Assert.DoesNotContain(MaskingSentinels.Secret, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SiNoRespondeDentroDelTiempoLimiteEsTimeout()
    {
        // RF-IA-06 / CA-HU12.1: "no responde dentro del tiempo límite".
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var started = DateTimeOffset.UtcNow;
        var ex = await Assert.ThrowsAsync<AiServiceUnavailableException>(() => Client(handler, timeoutSeconds: 1).SendAsync(Request(), CancellationToken.None));

        Assert.Equal(ExplanationCause.Timeout, ex.Cause);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task SiElServicioNoEstaAlcanzableEsServiceUnreachable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        var ex = await Assert.ThrowsAsync<AiServiceUnavailableException>(() => Client(handler).SendAsync(Request(), CancellationToken.None));

        Assert.Equal(ExplanationCause.ServiceUnreachable, ex.Cause);
    }

    [Fact]
    public async Task UnaRespuestaDemasiadoGrandeEsSalidaNoValida()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 4096)) });

        var ex = await Assert.ThrowsAsync<AiServiceUnavailableException>(() => Client(handler, maxBytes: 1024).SendAsync(Request(), CancellationToken.None));

        Assert.Equal(ExplanationCause.InvalidOutput, ex.Cause);
    }

    [Fact]
    public async Task SinDireccionConfiguradaNoSeIntentaLaLlamada()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<AiServiceUnavailableException>(() => Client(handler, baseUrl: null).SendAsync(Request(), CancellationToken.None));

        Assert.Equal(ExplanationCause.ServiceUnreachable, ex.Cause);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task LaCancelacionDelTrabajoNoSeConfundeConUnTimeout()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(handler).SendAsync(Request(), cts.Token));
    }

    [Theory]
    [InlineData("ftp://ai:8000/")]
    [InlineData("ai:8000")]
    public void UnaDireccionQueNoEsHttpSeRechazaAlArrancar(string baseUrl)
    {
        Assert.Throws<InvalidOperationException>(() => new AiServiceOptions { BaseUrl = baseUrl }.Validate());
    }

    [Fact]
    public void ElTiempoTotalPorDefectoCubreLosReintentosDelServicioDeIa()
    {
        // E1: 60 s × (1 + 2 reintentos) del servicio de IA (RNF-FIA-02), más margen.
        Assert.True(AiServiceOptions.DefaultTimeoutSeconds >= 60 * 3);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this((request, _) => Task.FromResult(respond(request)))
        {
        }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new SentRequest(
                request.Method,
                request.RequestUri!,
                request.Content?.Headers.ContentType?.MediaType,
                request.Headers.TryGetValues("X-Correlation-ID", out var values) ? values.Single() : null,
                body));
            return await _respond(request, cancellationToken);
        }
    }

    private sealed record SentRequest(HttpMethod Method, Uri Uri, string? ContentType, string? CorrelationId, string Body);
}
