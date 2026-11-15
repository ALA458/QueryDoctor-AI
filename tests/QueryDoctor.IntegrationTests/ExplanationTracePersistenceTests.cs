using QueryDoctor.Domain.Ai;
using QueryDoctor.Domain.Core;
using QueryDoctor.Infrastructure.Explanations;
using QueryDoctor.Infrastructure.Traceability;

namespace QueryDoctor.IntegrationTests;

// S2-06 contra PostgreSQL: el estado final de una explicación y su entrada de traza se guardan juntos (T2), con los
// campos de CA-HU14.1, y la traza sobrevive al borrado de la consulta (sin FK, §7).
[Collection(PostgresGroup.Name)]
public sealed class ExplanationTracePersistenceTests(PostgresFixture postgres)
{
    private async Task<Explanation> SeedRunningExplanationAsync()
    {
        await using var db = postgres.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Platform = ConnectionPlatform.SqlServer,
            Server = $"trz-{Guid.NewGuid():N}"[..20],
            DatabaseName = "QD_Lab",
            AuthenticationMethod = AuthenticationMethod.SqlAuthentication,
            Encryption = ConnectionEncryption.Mandatory,
            EncryptedCredentials = "no-se-usa",
            Status = ConnectionStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var query = new Query { Id = Guid.NewGuid(), ConnectionId = connection.Id, QueryStoreQueryId = Random.Shared.NextInt64(1, long.MaxValue), IngestedAt = now };
        var explanation = new Explanation
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            QueryId = query.Id,
            State = ExplanationState.Running,
            CorrelationId = Guid.NewGuid(),
            RequestedAt = now,
        };
        db.Connections.Add(connection);
        db.Queries.Add(query);
        db.Explanations.Add(explanation);
        await db.SaveChangesAsync();
        return explanation;
    }

    [Fact]
    public async Task ElEstadoFinalYSuEntradaDeTrazaSeGuardanJuntosConLosCamposDeCaHu141()
    {
        var seeded = await SeedRunningExplanationAsync();
        await using (var db = postgres.CreateContext())
        {
            var explanation = db.Explanations.Single(e => e.Id == seeded.Id);
            explanation.State = ExplanationState.Completed;
            explanation.ValidationStatus = "TRAZADA";
            explanation.FindingCodesJson = "[\"F-0001\",\"F-0002\"]";
            explanation.CatalogVersion = "1.0.0";
            explanation.Model = "gemma-3-27b";
            explanation.PromptVersion = "v1";
            explanation.LlmProfile = "mock";
            explanation.ContextHash = new string('c', 64);
            explanation.CompletedAt = DateTimeOffset.UtcNow;

            await new EfExplanationStore(db, TimeProvider.System).CompleteAsync(explanation, [], CancellationToken.None);
        }

        await using var read = postgres.CreateContext();
        var trace = read.TraceEntries.Single(t => t.ExplanationId == seeded.Id);
        Assert.Equal(seeded.QueryId, trace.QueryId);
        Assert.Equal("COMPLETADA", trace.State);
        Assert.Equal("TRAZADA", trace.ValidationStatus);
        Assert.Equal("[\"F-0001\",\"F-0002\"]", trace.FindingCodesJson);
        Assert.Equal("1.0.0", trace.CatalogVersion);
        Assert.Equal("gemma-3-27b", trace.Model);
        Assert.Equal("v1", trace.PromptVersion);
        Assert.Equal(new string('c', 64), trace.ContextHash);
        Assert.True(trace.RecordedAt > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.Equal(ExplanationState.Completed, read.Explanations.Single(e => e.Id == seeded.Id).State);
        Assert.True((await new EfTraceabilityQueries(read).VerifyTraceAsync(CancellationToken.None)).IsIntact);
    }

    [Fact]
    public async Task SiLaTrazaNoSePuedeEscribirTampocoSeGuardaElEstadoFinal()
    {
        // T2: todo o nada. Un estado final inválido para la traza (sigue en curso) hace fallar la transacción.
        var seeded = await SeedRunningExplanationAsync();
        await using (var db = postgres.CreateContext())
        {
            var explanation = db.Explanations.Single(e => e.Id == seeded.Id);
            explanation.ValidationStatus = "TRAZADA";

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new EfExplanationStore(db, TimeProvider.System).FinishAsync(explanation, CancellationToken.None));
        }

        await using var read = postgres.CreateContext();
        Assert.Null(read.Explanations.Single(e => e.Id == seeded.Id).ValidationStatus);
        Assert.False(read.TraceEntries.Any(t => t.ExplanationId == seeded.Id));
    }

    [Fact]
    public async Task LaTrazaSobreviveAlBorradoDeLaConsulta()
    {
        // §7: la traza no guarda SQL y no tiene FK, así que la purga de S2-13 no la borra.
        var seeded = await SeedRunningExplanationAsync();
        await using (var db = postgres.CreateContext())
        {
            var explanation = db.Explanations.Single(e => e.Id == seeded.Id);
            explanation.State = ExplanationState.Unavailable;
            explanation.Cause = ExplanationCause.Timeout;
            explanation.CompletedAt = DateTimeOffset.UtcNow;
            await new EfExplanationStore(db, TimeProvider.System).FinishAsync(explanation, CancellationToken.None);
        }

        await using (var db = postgres.CreateContext())
        {
            db.Queries.Remove(db.Queries.Single(q => q.Id == seeded.QueryId));
            await db.SaveChangesAsync();
        }

        await using var read = postgres.CreateContext();
        Assert.False(read.Explanations.Any(e => e.Id == seeded.Id));
        Assert.True(read.TraceEntries.Any(t => t.ExplanationId == seeded.Id));
    }
}
