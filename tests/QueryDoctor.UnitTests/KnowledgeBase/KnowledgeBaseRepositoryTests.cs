using QueryDoctor.Application.KnowledgeBase;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Infrastructure.Persistence;
using QueryDoctor.Infrastructure.Persistence.Seeds;

namespace QueryDoctor.UnitTests.KnowledgeBase;

public class KnowledgeBaseRepositoryTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EfRepositoryThrowsOnInvalidRuleId(string invalidRuleId)
    {
        var repository = new EfKnowledgeBaseRepository(null!);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetFragmentsByRuleAsync(invalidRuleId));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetCausalRelationsAsync(invalidRuleId));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.HasCausalRelationAsync(invalidRuleId, Guid.NewGuid(), CausalRelationType.Interpretacion));
    }

    [Fact]
    public async Task InMemoryRepositoryRetrievesFragmentsByRule()
    {
        var repository = new InMemoryKnowledgeBaseRepository();

        var fragmentsReg01 = await repository.GetFragmentsByRuleAsync("REG-01");

        Assert.Equal(2, fragmentsReg01.Count);
        Assert.All(fragmentsReg01, f => Assert.Equal("REG-01", f.RuleId));
    }

    [Fact]
    public async Task InMemoryRepositoryRetrievesFragmentById()
    {
        var repository = new InMemoryKnowledgeBaseRepository();

        var fragment = await repository.GetFragmentByIdAsync(KnowledgeBaseSeedData.FragmentReg04InterpretacionId);

        Assert.NotNull(fragment);
        Assert.Equal("REG-04", fragment.RuleId);
        Assert.Contains("Key Lookup", fragment.Title);
    }

    [Fact]
    public async Task InMemoryRepositoryReturnsNullWhenFragmentNotFound()
    {
        var repository = new InMemoryKnowledgeBaseRepository();

        var fragment = await repository.GetFragmentByIdAsync(Guid.NewGuid());

        Assert.Null(fragment);
    }

    [Fact]
    public async Task InMemoryRepositoryRetrievesCausalRelationsByRule()
    {
        var repository = new InMemoryKnowledgeBaseRepository();

        var relationsReg02 = await repository.GetCausalRelationsAsync("REG-02");

        Assert.Equal(2, relationsReg02.Count);
        Assert.Contains(relationsReg02, r => r.RelationType == CausalRelationType.Interpretacion);
        Assert.Contains(relationsReg02, r => r.RelationType == CausalRelationType.Recomendacion);
    }

    [Fact]
    public async Task HasCausalRelationAsyncValidatesMatchesAccurately()
    {
        var repository = new InMemoryKnowledgeBaseRepository();

        // 1. Coincidencia exacta de regla, fragmento y tipo
        var match = await repository.HasCausalRelationAsync(
            "REG-01",
            KnowledgeBaseSeedData.FragmentReg01InterpretacionId,
            CausalRelationType.Interpretacion);
        Assert.True(match);

        // 2. Mismo fragmento pero tipo de relación incorrecto
        var wrongType = await repository.HasCausalRelationAsync(
            "REG-01",
            KnowledgeBaseSeedData.FragmentReg01InterpretacionId,
            CausalRelationType.Recomendacion);
        Assert.False(wrongType);

        // 3. Regla incorrecta para el fragmento
        var wrongRule = await repository.HasCausalRelationAsync(
            "REG-02",
            KnowledgeBaseSeedData.FragmentReg01InterpretacionId,
            CausalRelationType.Interpretacion);
        Assert.False(wrongRule);

        // 4. Fragmento inexistente
        var notFound = await repository.HasCausalRelationAsync(
            "REG-01",
            Guid.NewGuid(),
            CausalRelationType.Interpretacion);
        Assert.False(notFound);
    }
}

internal sealed class InMemoryKnowledgeBaseRepository : IKnowledgeBaseRepository
{
    private readonly List<KbFragment> _fragments = [.. KnowledgeBaseSeedData.GetFragments()];
    private readonly List<KbCausalRelation> _relations = [.. KnowledgeBaseSeedData.GetCausalRelations()];

    public InMemoryKnowledgeBaseRepository()
    {
        // Enlazar relaciones con sus fragmentos en memoria
        var fragmentMap = _fragments.ToDictionary(f => f.Id);
        foreach (var relation in _relations)
        {
            if (fragmentMap.TryGetValue(relation.FragmentId, out var fragment))
            {
                relation.Fragment = fragment;
            }
        }
    }

    public Task<IReadOnlyList<KbFragment>> GetFragmentsByRuleAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);

        IReadOnlyList<KbFragment> result = _fragments
            .Where(f => f.RuleId.Equals(ruleId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Title)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<KbFragment?> GetFragmentByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var result = _fragments.FirstOrDefault(f => f.Id == id);
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<KbCausalRelation>> GetCausalRelationsAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);

        IReadOnlyList<KbCausalRelation> result = _relations
            .Where(r => r.RuleId.Equals(ruleId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.RelationType)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<bool> HasCausalRelationAsync(
        string ruleId,
        Guid fragmentId,
        CausalRelationType relationType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);

        var exists = _relations.Any(r =>
            r.RuleId.Equals(ruleId, StringComparison.OrdinalIgnoreCase) &&
            r.FragmentId == fragmentId &&
            r.RelationType == relationType);

        return Task.FromResult(exists);
    }
}
