using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Infrastructure.Persistence.Seeds;

namespace QueryDoctor.UnitTests.KnowledgeBase;

public class KnowledgeBaseSeedDataTests
{
    private static string[] GetRequiredImplementedAndMustRuleIds()
    {
        var implementedRuleIds = VersionedRuleCatalog.DiscoverBuiltInRules()
            .Select(r => r.Descriptor.RuleId);

        var mustRuleIds = VersionedRuleCatalog.BuiltInDescriptors
            .Where(d => d.Priority == RulePriority.Must)
            .Select(d => d.RuleId);

        return implementedRuleIds
            .Concat(mustRuleIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    [Fact]
    public void SeedFragmentsCoversEveryImplementedAndMustRuleInCatalog()
    {
        var requiredRules = GetRequiredImplementedAndMustRuleIds();
        var catalogRuleIds = VersionedRuleCatalog.BuiltInDescriptors
            .Select(d => d.RuleId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fragments = KnowledgeBaseSeedData.GetFragments();

        Assert.NotEmpty(requiredRules);
        Assert.Contains("REG-03", requiredRules);
        Assert.Contains("REG-11", requiredRules);

        foreach (var rule in requiredRules)
        {
            var ruleFragments = fragments.Where(f => f.RuleId == rule).ToList();
            Assert.True(
                ruleFragments.Count >= 1,
                $"La regla implementada o Debe '{rule}' debe tener al menos un fragmento curado en KnowledgeBaseSeedData.");
        }

        foreach (var fragment in fragments)
        {
            Assert.True(
                catalogRuleIds.Contains(fragment.RuleId),
                $"El fragmento {fragment.Id} referencia una regla inexistente en VersionedRuleCatalog: '{fragment.RuleId}'.");
        }

        // REG-03 (S2-01 / S2-08) cubre tanto el caso de predicado selectivo (a) como el de volumen y consumo (b).
        var reg03Fragments = fragments.Where(f => f.RuleId == "REG-03").ToList();
        Assert.True(reg03Fragments.Count >= 4, "REG-03 debe cubrir las condiciones (a) y (b) con interpretación y recomendación.");
        Assert.Contains(reg03Fragments, f => f.Id == KnowledgeBaseSeedData.FragmentReg03VolumenInterpretacionId);
        Assert.Contains(reg03Fragments, f => f.Id == KnowledgeBaseSeedData.FragmentReg03VolumenRecomendacionId);
    }

    [Fact]
    public void EveryFragmentHasValidMetadataAndOfficialSourceCitations()
    {
        var fragments = KnowledgeBaseSeedData.GetFragments();
        var fragmentIds = new HashSet<Guid>();

        foreach (var fragment in fragments)
        {
            Assert.NotEqual(Guid.Empty, fragment.Id);
            Assert.True(fragmentIds.Add(fragment.Id), $"Identificador duplicado para fragmento: {fragment.Id}");

            Assert.False(string.IsNullOrWhiteSpace(fragment.RuleId));
            Assert.False(string.IsNullOrWhiteSpace(fragment.Title));
            Assert.False(string.IsNullOrWhiteSpace(fragment.Content));
            Assert.False(string.IsNullOrWhiteSpace(fragment.SourceTitle));
            Assert.False(string.IsNullOrWhiteSpace(fragment.SourceCitation));
            Assert.False(string.IsNullOrWhiteSpace(fragment.SourceUrl));

            Assert.StartsWith("https://learn.microsoft.com", fragment.SourceUrl, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Microsoft Learn", fragment.SourceTitle, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SeedCausalRelationsCoversInterpretacionAndRecomendacionForEveryImplementedAndMustRule()
    {
        var requiredRules = GetRequiredImplementedAndMustRuleIds();
        var catalogRuleIds = VersionedRuleCatalog.BuiltInDescriptors
            .Select(d => d.RuleId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var relations = KnowledgeBaseSeedData.GetCausalRelations();
        var fragments = KnowledgeBaseSeedData.GetFragments().ToDictionary(f => f.Id);

        var relationKeys = new HashSet<string>();

        foreach (var rule in requiredRules)
        {
            var ruleRelations = relations.Where(r => r.RuleId == rule).ToList();

            Assert.Contains(ruleRelations, r => r.RelationType == CausalRelationType.Interpretacion);
            Assert.Contains(ruleRelations, r => r.RelationType == CausalRelationType.Recomendacion);
        }

        foreach (var relation in relations)
        {
            Assert.NotEqual(Guid.Empty, relation.Id);
            Assert.True(catalogRuleIds.Contains(relation.RuleId), $"Regla inexistente en catálogo para relación: {relation.RuleId}");
            Assert.True(fragments.TryGetValue(relation.FragmentId, out var fragment), $"Fragmento asociado no existe: {relation.FragmentId}");
            Assert.Equal(fragment.RuleId, relation.RuleId);

            var uniqueKey = $"{relation.RuleId}_{relation.FragmentId}_{relation.RelationType}";
            Assert.True(relationKeys.Add(uniqueKey), $"Relación causal duplicada detectada: {uniqueKey}");
        }
    }
}
