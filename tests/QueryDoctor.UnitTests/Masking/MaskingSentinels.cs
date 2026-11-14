namespace QueryDoctor.UnitTests.Masking;

/// <summary>
/// Valores centinela únicos de la suite de literales sembrados (S2-04, CA-HU21.1). Ninguno puede
/// aparecer en una salida enmascarada ni en una solicitud registrada.
/// </summary>
internal static class MaskingSentinels
{
    public const string Secret = "QD_SECRET_7f3a";
    public const string UnicodeSecret = "QD_ÑANDÚ_秘密_9c1e";
    public const string CommentSecret = "QD_COMMENT_b42d";
    public const string NestedCommentSecret = "QD_NESTED_e55a";
    public const string DynamicSqlSecret = "QD_DYNAMIC_31aa";
    public const string EscapedSecret = "QD_O''BRIEN_77ab";
    public const string EscapedSecretUnescaped = "QD_O'BRIEN_77ab";
    public const string DoubleQuotedSecret = "QD_DQ_5e0c";
    public const string BigNumber = "9876543210";
    public const string DecimalNumber = "31415.9265";
    public const string MoneyNumber = "8642.13";
    public const string Hex = "0xDEADBEEF";
    public const string HexBody = "DEADBEEF";
    public const string Date = "2026-01-01";
    public const string GuidValue = "6F9619FF-8B86-D011-B42D-00C04FC964FF";
    public const string CompiledValueSecret = "QD_COMPILED_a1b2";
    public const string RuntimeValueSecret = "QD_RUNTIME_c3d4";
    public const string CompiledNumber = "5550001";
    public const string RuntimeNumber = "5550002";
    public const string ConstSecret = "QD_CONST_e5f6";
    public const string PredicateSecret = "QD_PREDICATE_0a0b";
    public const string PredicateNumber = "7770007";

    /// <summary>
    /// Formas en que un centinela podría filtrarse. Se buscan sin distinguir mayúsculas y también la forma
    /// escapada en JSON o XML (la búsqueda es sobre texto plano y sobre el cuerpo serializado).
    /// </summary>
    public static readonly string[] All =
    [
        Secret,
        UnicodeSecret,
        CommentSecret,
        NestedCommentSecret,
        DynamicSqlSecret,
        EscapedSecretUnescaped,
        "QD_O''BRIEN",
        "BRIEN_77ab",
        DoubleQuotedSecret,
        BigNumber,
        DecimalNumber,
        MoneyNumber,
        HexBody,
        Date,
        GuidValue,
        CompiledValueSecret,
        RuntimeValueSecret,
        CompiledNumber,
        RuntimeNumber,
        ConstSecret,
        PredicateSecret,
        PredicateNumber,
    ];

    public static void AssertNoneIn(string? output)
    {
        Assert.NotNull(output);
        foreach (var sentinel in All)
        {
            Assert.True(
                !output.Contains(sentinel, StringComparison.OrdinalIgnoreCase),
                $"El centinela '{sentinel}' apareció en la salida enmascarada.");
        }
    }
}
