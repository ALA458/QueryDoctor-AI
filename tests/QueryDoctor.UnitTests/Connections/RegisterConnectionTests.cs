using QueryDoctor.Application.Connections;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.UnitTests.Connections;

// S1-09: alta de conexión (RF-CFG-01, RF-CFG-02, D3). El sondeo se prueba en P5.
public class RegisterConnectionTests
{
    private const string Password = "S3creta!Lab#2026";

    private readonly InMemoryConnectionRepository _repository = new();
    private readonly RecordingProbeScheduler _scheduler = new();
    private readonly FakeEncryptor _encryptor = new();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero));

    private RegisterConnection UseCase() => new(_repository, _scheduler, _encryptor, _clock);

    private static RegisterConnectionCommand Valid(string server = "tcp:SQLSERVER,1433", string database = "QD_Lab") =>
        new("SqlServer", server, database, "SqlAuthentication", "Mandatory", "qd_reader", Password);

    [Fact]
    public async Task UnAltaValidaQuedaPendienteNormalizadaYConElSondeoEncolado()
    {
        var result = await UseCase().ExecuteAsync(Valid());

        var accepted = Assert.IsType<RegisterConnectionResult.Accepted>(result);
        Assert.False(accepted.ReplacedPrevious);

        var saved = Assert.Single(_repository.Stored);
        Assert.Equal(accepted.ConnectionId, saved.Id);
        Assert.Equal("sqlserver", saved.Server);
        Assert.Equal("QD_Lab", saved.DatabaseName);
        Assert.Equal(ConnectionStatus.Pending, saved.Status);
        Assert.Null(saved.ProbeFailureCause);
        Assert.Equal(_clock.Now, saved.CreatedAt);

        Assert.Equal([saved.Id], _scheduler.Enqueued);
    }

    [Fact]
    public async Task LasCredencialesSeGuardanCifradasYSeRecuperanIntactas()
    {
        await UseCase().ExecuteAsync(Valid());
        var saved = Assert.Single(_repository.Stored);

        Assert.NotNull(saved.EncryptedCredentials);
        Assert.DoesNotContain(Password, saved.EncryptedCredentials, StringComparison.Ordinal);
        Assert.DoesNotContain("qd_reader", saved.EncryptedCredentials, StringComparison.Ordinal);

        var credentials = ConnectionCredentialsCodec.Decrypt(saved.EncryptedCredentials, _encryptor);
        Assert.Equal(new ConnectionCredentials("qd_reader", Password), credentials);
    }

    [Fact]
    public async Task LaContrasenaNoApareceEnNingunaRepresentacionDeTexto()
    {
        var command = Valid();
        var result = await UseCase().ExecuteAsync(command);

        Assert.DoesNotContain(Password, command.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Password, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Password, new ConnectionCredentials("u", Password).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnAltaInvalidaDevuelveErroresPorCampoYNoGuardaNiEncola()
    {
        var command = new RegisterConnectionCommand("Oracle", "np:srv", " ", "Kerberos", "None", "", "");

        var result = await UseCase().ExecuteAsync(command);

        var invalid = Assert.IsType<RegisterConnectionResult.Invalid>(result);
        Assert.Equal(
            ["authenticationMethod", "databaseName", "encryption", "password", "platform", "server", "username"],
            invalid.Errors.Keys.Order(StringComparer.Ordinal));
        Assert.Contains("np:", invalid.Errors["server"][0], StringComparison.Ordinal);
        Assert.Empty(_repository.Stored);
        Assert.Empty(_scheduler.Enqueued);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("99")]
    public async Task NoAceptaEnumeradosComoNumero(string platform)
    {
        var result = await UseCase().ExecuteAsync(Valid() with { Platform = platform });

        var invalid = Assert.IsType<RegisterConnectionResult.Invalid>(result);
        Assert.True(invalid.Errors.ContainsKey("platform"));
    }

    [Fact]
    public async Task SinCifradoIndicadoUsaMandatory()
    {
        await UseCase().ExecuteAsync(Valid() with { Encryption = null });

        Assert.Equal(ConnectionEncryption.Mandatory, Assert.Single(_repository.Stored).Encryption);
    }

    [Theory]
    [InlineData(ConnectionStatus.Active)]
    [InlineData(ConnectionStatus.Pending)]
    [InlineData(ConnectionStatus.Disabled)]
    public async Task UnaBaseYaRegistradaConOtraCapitalizacionEsConflicto(ConnectionStatus status)
    {
        await UseCase().ExecuteAsync(Valid());
        var first = Assert.Single(_repository.Stored);
        first.Status = status;
        _scheduler.Enqueued.Clear();

        var result = await UseCase().ExecuteAsync(Valid(server: "SqlServer", database: "qd_lab"));

        var conflict = Assert.IsType<RegisterConnectionResult.Conflict>(result);
        Assert.Equal(first.Id, conflict.ExistingConnectionId);
        Assert.Equal(status, conflict.ExistingStatus);
        Assert.Single(_repository.Stored);
        Assert.Empty(_scheduler.Enqueued);
    }

    [Theory]
    [InlineData(ConnectionStatus.Failed, ProbeFailureCause.InvalidCredentials)]
    [InlineData(ConnectionStatus.Rejected, ProbeFailureCause.UnsupportedVersion)]
    public async Task UnRegistroFallidoORechazadoSeReemplazaYSeVuelveASondear(ConnectionStatus status, ProbeFailureCause cause)
    {
        await UseCase().ExecuteAsync(Valid());
        var first = Assert.Single(_repository.Stored);
        first.Status = status;
        first.ProbeFailureCause = cause;
        first.LastProbedAt = _clock.Now;
        first.EncryptedCredentials = status == ConnectionStatus.Rejected ? null : first.EncryptedCredentials;
        _scheduler.Enqueued.Clear();
        _clock.Now = _clock.Now.AddMinutes(5);

        var result = await UseCase().ExecuteAsync(Valid() with { Password = "Otra!Clave#2026" });

        var accepted = Assert.IsType<RegisterConnectionResult.Accepted>(result);
        Assert.True(accepted.ReplacedPrevious);
        Assert.Equal(first.Id, accepted.ConnectionId);

        var saved = Assert.Single(_repository.Stored);
        Assert.Equal(ConnectionStatus.Pending, saved.Status);
        Assert.Null(saved.ProbeFailureCause);
        Assert.Null(saved.LastProbedAt);
        Assert.Equal(_clock.Now, saved.UpdatedAt);
        Assert.Equal("Otra!Clave#2026", ConnectionCredentialsCodec.Decrypt(saved.EncryptedCredentials!, _encryptor).Password);
        Assert.Equal([first.Id], _scheduler.Enqueued);
    }

    [Fact]
    public async Task UnaCarreraContraLaClaveUnicaTerminaEnConflicto()
    {
        var winner = new Connection { Id = Guid.NewGuid(), Server = "sqlserver", DatabaseName = "QD_LAB", Status = ConnectionStatus.Pending };
        _repository.RaceWinner = winner;

        var result = await UseCase().ExecuteAsync(Valid());

        var conflict = Assert.IsType<RegisterConnectionResult.Conflict>(result);
        Assert.Equal(winner.Id, conflict.ExistingConnectionId);
        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public async Task SiNoSePuedeEncolarLaConexionQuedaFailedYNoPendingParaSiempre()
    {
        _scheduler.Fail = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => UseCase().ExecuteAsync(Valid()));

        var saved = Assert.Single(_repository.Stored);
        Assert.Equal(ConnectionStatus.Failed, saved.Status);
        Assert.Equal(ProbeFailureCause.Unknown, saved.ProbeFailureCause);
        Assert.NotNull(saved.LastProbedAt);
    }
}
