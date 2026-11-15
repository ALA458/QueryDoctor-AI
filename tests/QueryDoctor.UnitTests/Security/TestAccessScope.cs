using QueryDoctor.Application.Common.Security;

namespace QueryDoctor.UnitTests.Security;

/// <summary>Contexto de acceso fijo para pruebas (S2-12). El Sistema real es <see cref="SystemAccessScope.Instance"/>.</summary>
public sealed class TestAccessScope : IAccessScope
{
    private TestAccessScope(AccessKind kind, Guid[] assigned)
    {
        Kind = kind;
        AssignedConnectionIds = assigned;
    }

    public static TestAccessScope None { get; } = new(AccessKind.None, []);

    public static TestAccessScope Dba { get; } = new(AccessKind.Dba, []);

    public AccessKind Kind { get; }

    public IReadOnlyCollection<Guid> AssignedConnectionIds { get; }

    public static TestAccessScope Developer(params Guid[] assigned) => new(AccessKind.Developer, assigned);
}
