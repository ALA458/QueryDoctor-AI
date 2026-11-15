using Npgsql;
using QueryDoctor.Application.Traceability;
using QueryDoctor.Domain.Audit;
using QueryDoctor.Infrastructure.Traceability;

namespace QueryDoctor.IntegrationTests;

// S2-06 contra PostgreSQL real: inserción serializada, solo adición en la base (trigger) y el "Hecho cuando":
// la verificación de la cadena detecta una fila alterada a mano. La alteración se hace como lo haría alguien con
// privilegios: saltando el trigger con session_replication_role = replica. Cada prueba deja la cadena como estaba.
[Collection(PostgresGroup.Name)]
public sealed class TraceabilityPersistenceTests(PostgresFixture postgres)
{
    private async Task<long> AppendAsync(AuditEventType type, string userName)
    {
        await using var db = postgres.CreateContext();
        await new EfAuditLog(db, TimeProvider.System).AppendAsync(
            new AuditEventDraft(type, Guid.NewGuid(), userName, AuditEventDraft.OutcomeOk), CancellationToken.None);
        await using var read = postgres.CreateContext();
        return read.AuditEvents.Max(e => e.Seq);
    }

    private async Task<ChainVerification> VerifyAuditAsync()
    {
        await using var db = postgres.CreateContext();
        return await new EfTraceabilityQueries(db).VerifyAuditAsync(CancellationToken.None);
    }

    private async Task ExecuteAsync(string sql, bool bypassTrigger = false)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        if (bypassTrigger)
        {
            await using var replica = new NpgsqlCommand("SET session_replication_role = replica", connection);
            await replica.ExecuteNonQueryAsync();
        }

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task LosEventosSeEncadenanConSeqConsecutivaYLaCadenaVerifica()
    {
        var first = await AppendAsync(AuditEventType.LoginSucceeded, "dba");
        var second = await AppendAsync(AuditEventType.Logout, "dba");

        Assert.Equal(first + 1, second);
        await using var db = postgres.CreateContext();
        var a = db.AuditEvents.Single(e => e.Seq == first);
        var b = db.AuditEvents.Single(e => e.Seq == second);
        Assert.Equal(a.EntryHash, b.PrevHash);

        // La fecha que vuelve de PostgreSQL produce el mismo hash que se guardó (microsegundos).
        Assert.Equal(b.EntryHash, HashChain.Compute(b.PrevHash, b));
        Assert.True((await VerifyAuditAsync()).IsIntact);
    }

    [Theory]
    [InlineData("UPDATE audit.audit_event SET user_name = 'otro' WHERE seq = {0}")]
    [InlineData("DELETE FROM audit.audit_event WHERE seq = {0}")]
    [InlineData("TRUNCATE audit.audit_event")]
    public async Task LaBaseRechazaModificarOBorrarEntradas(string sqlTemplate)
    {
        // RF-TRZ-02 en la base, no solo en la aplicación.
        var seq = await AppendAsync(AuditEventType.LoginSucceeded, "dba");

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, sqlTemplate, seq)));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
        Assert.True((await VerifyAuditAsync()).IsIntact);
    }

    [Theory]
    [InlineData("UPDATE audit.trace_entry SET model = 'otro' WHERE seq = {0}")]
    [InlineData("DELETE FROM audit.trace_entry WHERE seq = {0}")]
    [InlineData("TRUNCATE audit.trace_entry")]
    public async Task LaBaseRechazaModificarOBorrarLaTraza(string sqlTemplate)
    {
        long seq;
        await using (var db = postgres.CreateContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var entry = new TraceEntry
            {
                ExplanationId = Guid.NewGuid(),
                QueryId = Guid.NewGuid(),
                ConnectionId = Guid.NewGuid(),
                State = "COMPLETADA",
                ValidationStatus = "TRAZADA",
            };
            await ChainWriter.AppendTraceAsync(db, entry, DateTimeOffset.UtcNow, CancellationToken.None);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            seq = entry.Seq;
        }

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, sqlTemplate, seq)));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
        await using var read = postgres.CreateContext();
        Assert.True((await new EfTraceabilityQueries(read).VerifyTraceAsync(CancellationToken.None)).IsIntact);
    }

    [Fact]
    public async Task UnaFilaAlteradaAManoSeDetectaEnLaVerificacion()
    {
        // "Hecho cuando" de S2-06.
        var seq = await AppendAsync(AuditEventType.ConnectionRegistered, "dba");
        await AppendAsync(AuditEventType.ManualIngestionRequested, "dba");

        await ExecuteAsync($"UPDATE audit.audit_event SET outcome = 'rejected' WHERE seq = {seq}", bypassTrigger: true);
        try
        {
            var result = await VerifyAuditAsync();

            Assert.False(result.IsIntact);
            Assert.Equal(seq, result.FirstBrokenSeq);
            Assert.Equal(ChainVerifier.ReasonHashMismatch, result.Reason);
        }
        finally
        {
            await ExecuteAsync($"UPDATE audit.audit_event SET outcome = 'ok' WHERE seq = {seq}", bypassTrigger: true);
        }

        Assert.True((await VerifyAuditAsync()).IsIntact);
    }

    [Fact]
    public async Task UnaFilaBorradaAManoSeDetectaEnLaSiguiente()
    {
        var removed = await AppendAsync(AuditEventType.LoginFailed, "dba");
        var next = await AppendAsync(AuditEventType.LoginSucceeded, "dba");
        await using (var db = postgres.CreateContext())
        {
            var row = db.AuditEvents.Single(e => e.Seq == removed);
            await ExecuteAsync($"DELETE FROM audit.audit_event WHERE seq = {removed}", bypassTrigger: true);
            try
            {
                var result = await VerifyAuditAsync();

                Assert.Equal(next, result.FirstBrokenSeq);
                Assert.Equal(ChainVerifier.ReasonBrokenLink, result.Reason);
            }
            finally
            {
                await ExecuteAsync(
                    "INSERT INTO audit.audit_event (seq, recorded_at, event_type, user_id, user_name, subject_type, subject_id, outcome, prev_hash, entry_hash) " +
                    $"VALUES ({row.Seq}, '{row.RecordedAt.UtcDateTime:yyyy-MM-dd HH:mm:ss.ffffff}+00', '{row.EventType}', '{row.UserId}', '{row.UserName}', NULL, NULL, '{row.Outcome}', '{row.PrevHash}', '{row.EntryHash}')",
                    bypassTrigger: true);
            }
        }

        Assert.True((await VerifyAuditAsync()).IsIntact);
    }

    [Fact]
    public async Task EscriturasConcurrentesNoBifurcanLaCadena()
    {
        // Inserción serializada: Api y Worker escriben a la vez sin repetir seq ni romper prev_hash.
        var start = await AppendAsync(AuditEventType.LoginSucceeded, "inicio");
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => AppendAsync(AuditEventType.LoginSucceeded, $"u{i}")));

        await using var db = postgres.CreateContext();
        var seqs = db.AuditEvents.Where(e => e.Seq > start).Select(e => e.Seq).OrderBy(s => s).ToList();
        Assert.Equal(Enumerable.Range(1, 20).Select(i => start + i), seqs);
        Assert.True((await VerifyAuditAsync()).IsIntact);
    }

    [Fact]
    public async Task SinTransaccionNoSeEscribeEnLaCadena()
    {
        await using var db = postgres.CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ChainWriter.AppendAuditAsync(db, new AuditEvent { EventType = AuditEventType.Logout, Outcome = "ok" }, DateTimeOffset.UtcNow, CancellationToken.None));
    }
}
