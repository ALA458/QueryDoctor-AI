using System.Globalization;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace QueryDoctor.Application.Masking;

/// <summary>
/// Sustitución léxica de literales y borrado de comentarios con el tokenizador oficial de T-SQL
/// (ScriptDom, RNF-SEG-05). Sin expresiones regulares ni camino de respaldo (RA-03): si el lexer
/// informa cualquier error, el texto completo es no enviable.
/// </summary>
internal static class SqlLexicalMasker
{
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd",
        "yyyyMMdd",
        "yyyy-MM-ddTHH:mm",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.FFFFFFF",
        "yyyy-MM-ddTHH:mm:sszzz",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFFzzz",
        "yyyy-MM-dd HH:mm:sszzz",
        "yyyy-MM-dd HH:mm:ss.FFFFFFFzzz",
        "yyyyMMdd HH:mm:ss",
        "yyyyMMdd HH:mm:ss.FFFFFFF",
        "HH:mm:ss",
        "HH:mm:ss.FFFFFFF",
    ];

    /// <summary>
    /// Enmascara <paramref name="text"/>. Devuelve <see langword="false"/> ante cualquier error léxico;
    /// en ese caso <paramref name="masked"/> es <see langword="null"/>. El mensaje de ScriptDom se descarta
    /// a propósito porque repite el literal que no pudo cerrar.
    /// </summary>
    public static bool TryMask(string text, MaskingSession session, out string? masked)
    {
        masked = null;

        // TSql170Parser (SQL Server 2025, D7): para tokenizar, una versión más nueva solo reconoce más palabras
        // clave. QUOTED_IDENTIFIER da igual porque el texto entre comillas dobles se enmascara siempre (D4).
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        IList<TSqlParserToken> tokens;
        using (var reader = new StringReader(text))
        {
            tokens = parser.GetTokenStream(reader, out var errors);
            if (errors.Count > 0 || tokens is null)
            {
                return false;
            }
        }

        var builder = new StringBuilder(text.Length);
        foreach (var token in tokens)
        {
            if (token is null || token.TokenType == TSqlTokenType.EndOfFile)
            {
                continue;
            }

            switch (token.TokenType)
            {
                case TSqlTokenType.SingleLineComment:
                case TSqlTokenType.MultilineComment:
                    // El comentario se elimina completo (RNF-SEG-05, CA-HU21.2). El espacio evita pegar tokens vecinos.
                    session.CountCommentRemoved();
                    builder.Append(' ');
                    break;

                case TSqlTokenType.AsciiStringLiteral:
                case TSqlTokenType.UnicodeStringLiteral:
                    builder.Append(session.MarkerFor(ClassifyString(UnquoteSingle(token.Text)), token.Text));
                    break;

                case TSqlTokenType.AsciiStringOrQuotedIdentifier:
                    // Con QUOTED_IDENTIFIER OFF es una cadena: se trata siempre como literal (D4).
                    builder.Append(session.MarkerFor(ClassifyString(UnquoteDouble(token.Text)), token.Text));
                    break;

                case TSqlTokenType.Integer:
                case TSqlTokenType.Numeric:
                case TSqlTokenType.Real:
                case TSqlTokenType.Money:
                    builder.Append(session.MarkerFor(LiteralKind.Number, token.Text));
                    break;

                case TSqlTokenType.HexLiteral:
                    builder.Append(session.MarkerFor(LiteralKind.Binary, token.Text));
                    break;

                default:
                    builder.Append(token.Text);
                    break;
            }
        }

        masked = builder.ToString();
        return true;
    }

    private static LiteralKind ClassifyString(string content)
    {
        var trimmed = content.Trim();
        if (Guid.TryParse(trimmed, out _))
        {
            return LiteralKind.UniqueId;
        }

        if (DateTimeOffset.TryParseExact(trimmed, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return LiteralKind.Temporal;
        }

        return LiteralKind.Text;
    }

    private static string UnquoteSingle(string tokenText)
    {
        var start = tokenText.StartsWith('N') || tokenText.StartsWith('n') ? 2 : 1;
        if (tokenText.Length < start + 1)
        {
            return string.Empty;
        }

        return tokenText[start..^1].Replace("''", "'", StringComparison.Ordinal);
    }

    private static string UnquoteDouble(string tokenText) =>
        tokenText.Length < 2 ? string.Empty : tokenText[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
}
