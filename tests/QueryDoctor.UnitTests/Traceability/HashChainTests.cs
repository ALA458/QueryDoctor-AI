using QueryDoctor.Application.Traceability;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Domain.Audit;

namespace QueryDoctor.UnitTests.Traceability;

/// <summary>
/// Cadena de hash de la traza y la auditoría (S2-06, RF-TRZ-02): determinista y capaz de detectar una fila alterada,
/// borrada o reordenada ("Hecho cuando").
/// </summary>
public sealed class HashChainTests
{
    private static readonly DateTimeOffset Base = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    internal static List<TraceEntry> TraceChain(int count)
    {
        var entries = new List<TraceEntry>();
        var prev = HashChain.Genesis;
        for (var i = 1; i <= count; i++)
        {
            var entry = new TraceEntry
            {
                Seq = i,
                RecordedAt = HashChain.Normalize(Base.AddSeconds(i).AddTicks(7)),
                ExplanationId = Guid.NewGuid(),
                QueryId = Guid.NewGuid(),
                ConnectionId = Guid.NewGuid(),
                State = "COMPLETADA",
                ValidationStatus = "TRAZADA",
                FindingCodesJson = "[\"F-0001\"]",
                CatalogVersion = "1.0.0",
                Model = "mock-model",
                PromptVersion = "v1",
                ContextHash = new string('a', 64),
                PrevHash = prev,
            };
            entry.EntryHash = HashChain.Compute(prev, entry);
            prev = entry.EntryHash;
            entries.Add(entry);
        }

        return entries;
    }

    [Fact]
    public void UnaCadenaBienFormadaEstaIntacta()
    {
        var result = ChainVerifier.Verify(TraceChain(5));

        Assert.True(result.IsIntact);
        Assert.Equal(5, result.EntriesChecked);
        Assert.Null(result.FirstBrokenSeq);
    }

    [Fact]
    public void UnaCadenaVaciaEstaIntacta()
    {
        Assert.True(ChainVerifier.Verify(Array.Empty<TraceEntry>()).IsIntact);
    }

    [Fact]
    public void ElMismoContenidoDaElMismoHashYUnCampoDistintoOtro()
    {
        var entry = TraceChain(1)[0];

        Assert.Equal(entry.EntryHash, HashChain.Compute(HashChain.Genesis, entry));
        entry.ValidationStatus = "NO_VERIFICADA";
        Assert.NotEqual(entry.EntryHash, HashChain.Compute(HashChain.Genesis, entry));
    }

    [Fact]
    public void UnaFilaAlteradaSeDetectaEnEsaFila()
    {
        var chain = TraceChain(5);
        chain[2].ValidationStatus = "NO_VERIFICADA";

        var result = ChainVerifier.Verify(chain);

        Assert.False(result.IsIntact);
        Assert.Equal(3, result.FirstBrokenSeq);
        Assert.Equal(ChainVerifier.ReasonHashMismatch, result.Reason);
    }

    [Fact]
    public void UnaFilaAlteradaConSuHashRecalculadoRompeElEnlaceSiguiente()
    {
        // Quien altera una fila y recalcula su hash no puede rehacer la cadena sin tocar las siguientes.
        var chain = TraceChain(5);
        chain[2].Model = "otro";
        chain[2].EntryHash = HashChain.Compute(chain[2].PrevHash, chain[2]);

        var result = ChainVerifier.Verify(chain);

        Assert.Equal(4, result.FirstBrokenSeq);
        Assert.Equal(ChainVerifier.ReasonBrokenLink, result.Reason);
    }

    [Fact]
    public void UnaFilaBorradaSeDetecta()
    {
        var chain = TraceChain(5);
        chain.RemoveAt(1);

        var result = ChainVerifier.Verify(chain);

        Assert.Equal(3, result.FirstBrokenSeq);
        Assert.Equal(ChainVerifier.ReasonBrokenLink, result.Reason);
    }

    [Fact]
    public void BorrarLaPrimeraFilaSeDetecta()
    {
        var chain = TraceChain(3);
        chain.RemoveAt(0);

        Assert.Equal(2, ChainVerifier.Verify(chain).FirstBrokenSeq);
    }

    [Fact]
    public void LaFechaSeRecortaAMicrosegundosParaQueCoincidaConPostgres()
    {
        var withTicks = new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero).AddTicks(1_234_567);

        var normalized = HashChain.Normalize(withTicks);

        Assert.Equal(0, normalized.Ticks % 10);
        Assert.Equal(withTicks.Ticks - 7, normalized.Ticks);
        Assert.Equal(TimeSpan.Zero, normalized.Offset);
    }

    [Fact]
    public void LaAuditoriaSeEncadenaYSeVerificaIgual()
    {
        var first = new AuditEvent { Seq = 1, RecordedAt = HashChain.Normalize(Base), EventType = AuditEventType.LoginSucceeded, UserName = "dba", Outcome = "ok", PrevHash = HashChain.Genesis };
        first.EntryHash = HashChain.Compute(first.PrevHash, first);
        var second = new AuditEvent { Seq = 2, RecordedAt = HashChain.Normalize(Base.AddSeconds(1)), EventType = AuditEventType.LoginFailed, UserName = "dba", Outcome = "rejected", PrevHash = first.EntryHash };
        second.EntryHash = HashChain.Compute(second.PrevHash, second);

        Assert.True(ChainVerifier.Verify([first, second]).IsIntact);

        second.UserName = "otro";
        Assert.Equal(2, ChainVerifier.Verify([first, second]).FirstBrokenSeq);
    }

    [Fact]
    public void LaEntradaDeTrazaCopiaDeLaExplicacionLoQuePideRfTrz01()
    {
        var explanation = new Explanation
        {
            Id = Guid.NewGuid(),
            QueryId = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            State = ExplanationState.Completed,
            ValidationStatus = "TRAZADA",
            FindingCodesJson = "[\"F-0001\"]",
            CatalogVersion = "1.0.0",
            Model = "modelo",
            PromptVersion = "v1",
            LlmProfile = "mock",
            ServedBy = "mock",
            DataPolicy = "lab_only",
            ContextHash = new string('b', 64),
        };

        var entry = TraceEntries.FromExplanation(explanation);

        Assert.Equal(explanation.Id, entry.ExplanationId);
        Assert.Equal("COMPLETADA", entry.State);
        Assert.Equal("TRAZADA", entry.ValidationStatus);
        Assert.Equal("[\"F-0001\"]", entry.FindingCodesJson);
        Assert.Equal("1.0.0", entry.CatalogVersion);
        Assert.Equal("modelo", entry.Model);
        Assert.Equal("v1", entry.PromptVersion);
        Assert.Equal(explanation.ContextHash, entry.ContextHash);
    }

    [Theory]
    [InlineData(ExplanationState.Pending)]
    [InlineData(ExplanationState.Running)]
    public void UnaExplicacionSinTerminarNoEntraEnLaTraza(ExplanationState state)
    {
        Assert.Throws<InvalidOperationException>(() => TraceEntries.FromExplanation(new Explanation { State = state }));
    }
}
