using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace QueryDoctor.ArchitectureTests;

/// <summary>
/// S2-12 (RNF-SEG-12): nadie se salta el filtro por conexión. <c>IgnoreQueryFilters</c> no hace falta en ningún lado
/// (el contexto Sistema del Worker ya ve todo), así que la regla es la más estricta: cero llamadas en <c>src/</c>. Se
/// revisa el IL, no el texto, para que un alias o un <c>using static</c> no la esquiven.
/// </summary>
public sealed class AccessScopeArchitectureTests
{
    private static readonly string[] ProductionAssemblies =
    [
        "QueryDoctor.Domain",
        "QueryDoctor.Application",
        "QueryDoctor.Infrastructure",
        "QueryDoctor.Api",
        "QueryDoctor.Worker",
    ];

    [Fact]
    public void NingunEnsambladoDeProduccionLlamaAIgnoreQueryFilters()
    {
        var callers = ProductionAssemblies.SelectMany(name => CallersOfIgnoreQueryFilters(Assembly.Load(name))).ToList();

        Assert.True(callers.Count == 0, $"IgnoreQueryFilters salta el filtro por conexión (S2-12): {string.Join(", ", callers)}");
    }

    [Fact]
    public void LaReglaDetectaUnaLlamada()
    {
        // Canario: este ensamblado de pruebas sí la llama (abajo). Si la regla no lo ve, la regla está rota.
        var callers = CallersOfIgnoreQueryFilters(typeof(AccessScopeArchitectureTests).Assembly);

        Assert.Contains(callers, c => c.Contains(nameof(CanaryThatSkipsTheFilter), StringComparison.Ordinal));
    }

    private static IQueryable<string> CanaryThatSkipsTheFilter() => Array.Empty<string>().AsQueryable().IgnoreQueryFilters();

    private static List<string> CallersOfIgnoreQueryFilters(Assembly assembly)
    {
        using var module = ModuleDefinition.ReadModule(assembly.Location);

        return AllTypes(module.Types)
            .SelectMany(t => t.Methods)
            .Where(m => m.HasBody && m.Body.Instructions.Any(IsIgnoreQueryFiltersCall))
            .Select(m => $"{m.DeclaringType.FullName}::{m.Name}")
            .ToList();
    }

    private static bool IsIgnoreQueryFiltersCall(Instruction instruction) =>
        (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt || instruction.OpCode == OpCodes.Ldftn)
        && instruction.Operand is MethodReference method
        && method.Name == nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters)
        && method.DeclaringType.FullName == typeof(EntityFrameworkQueryableExtensions).FullName;

    // Incluye los tipos anidados: las lambdas y las máquinas de estado de async viven ahí.
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types) =>
        types.SelectMany(t => AllTypes(t.NestedTypes).Prepend(t));
}
