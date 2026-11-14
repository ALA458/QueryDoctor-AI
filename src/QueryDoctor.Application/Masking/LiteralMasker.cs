namespace QueryDoctor.Application.Masking;

/// <summary>
/// Implementación de <see cref="IMasker"/> con ScriptDom (RNF-SEG-05, ADR-05). No recibe configuración:
/// no existe forma de desactivarla ni de cambiar qué se enmascara (CA-HU21.4).
/// </summary>
public sealed class LiteralMasker : IMasker
{
    /// <summary>
    /// Tamaño máximo del texto SQL que se tokeniza (D8). Constante a propósito: no es configurable.
    /// </summary>
    public const int MaxSqlCharacters = 1_048_576;

    /// <inheritdoc />
    public MaskedContext Mask(MaskingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var session = new MaskingSession();
        var sql = MaskSql(input.SqlText, session);
        var plan = MaskPlan(input.PlanXml, session);

        return new MaskedContext(sql, plan, session.ToMetrics());
    }

    internal static MaskedTextResult MaskPlan(string? planXml, MaskingSession session)
    {
        if (string.IsNullOrWhiteSpace(planXml))
        {
            return MaskedTextResult.NotSendable(MaskingFailureCause.InputMissing);
        }

        var checkpoint = session.Save();
        if (!PlanXmlMasker.TryMask(planXml, session, out var masked, out var cause))
        {
            session.Restore(checkpoint);
            return MaskedTextResult.NotSendable(cause!.Value);
        }

        return MaskedTextResult.Sendable(masked!);
    }

    internal static MaskedTextResult MaskSql(string? sqlText, MaskingSession session)
    {
        if (sqlText is null)
        {
            return MaskedTextResult.NotSendable(MaskingFailureCause.InputMissing);
        }

        if (sqlText.Length > MaxSqlCharacters)
        {
            return MaskedTextResult.NotSendable(MaskingFailureCause.InputTooLarge);
        }

        var checkpoint = session.Save();
        if (!SqlLexicalMasker.TryMask(sqlText, session, out var masked))
        {
            session.Restore(checkpoint);
            return MaskedTextResult.NotSendable(MaskingFailureCause.SqlLexicalError);
        }

        return MaskedTextResult.Sendable(masked!);
    }
}
