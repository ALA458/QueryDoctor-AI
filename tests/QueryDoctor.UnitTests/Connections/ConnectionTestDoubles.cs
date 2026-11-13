using System.Text;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Application.Connections;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.UnitTests.Connections;

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Cifrado reversible de prueba: el texto en claro nunca aparece literal en el resultado.</summary>
internal sealed class FakeEncryptor : IFieldEncryptor
{
    private const string Prefix = "enc:";

    public string Encrypt(string plainText) =>
        Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(new string(plainText.Reverse().ToArray())));

    public string Decrypt(string cipherText) =>
        new(Encoding.UTF8.GetString(Convert.FromBase64String(cipherText[Prefix.Length..])).Reverse().ToArray());

    public byte[] Encrypt(ReadOnlySpan<byte> plainBytes) => plainBytes.ToArray();

    public byte[] Decrypt(ReadOnlySpan<byte> cipherBytes) => cipherBytes.ToArray();
}

internal sealed class InMemoryConnectionRepository : IConnectionRepository
{
    public List<Connection> Stored { get; } = [];

    public int SaveCount { get; private set; }

    /// <summary>Simula una carrera: al guardar, otra solicitud ya insertó esta conexión.</summary>
    public Connection? RaceWinner { get; set; }

    private readonly List<Connection> _pending = [];

    public Task<Connection?> FindByAddressAsync(string normalizedServer, string databaseName, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.FirstOrDefault(c =>
            c.Server == normalizedServer
            && ConnectionAddress.DatabaseNameKey(c.DatabaseName) == ConnectionAddress.DatabaseNameKey(databaseName)));

    public Task<Connection?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.FirstOrDefault(c => c.Id == id));

    public void Add(Connection connection) => _pending.Add(connection);

    public List<CapabilityProfile> Profiles { get; } = [];

    public Task<CapabilityProfile?> GetProfileAsync(Guid connectionId, CancellationToken cancellationToken) =>
        Task.FromResult(Profiles.FirstOrDefault(p => p.ConnectionId == connectionId));

    public void AddProfile(CapabilityProfile profile) => Profiles.Add(profile);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (RaceWinner is not null && _pending.Count > 0)
        {
            Stored.Add(RaceWinner);
            RaceWinner = null;
            _pending.Clear();
            throw new DuplicateConnectionException("UK");
        }

        Stored.AddRange(_pending);
        _pending.Clear();
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class RecordingProbeScheduler : IProbeJobScheduler
{
    public List<Guid> Enqueued { get; } = [];

    public bool Fail { get; set; }

    public void EnqueueProbe(Guid connectionId)
    {
        if (Fail)
        {
            throw new InvalidOperationException("Hangfire no disponible");
        }

        Enqueued.Add(connectionId);
    }
}
