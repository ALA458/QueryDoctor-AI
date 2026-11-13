using QueryDoctor.Domain.Ai;
using QueryDoctor.Infrastructure.Persistence.Seeds;

namespace QueryDoctor.UnitTests.KnowledgeBase;

public class KnowledgeBaseSeedDataTests
{
    private static readonly string[] RequiredRules = ["REG-01", "REG-02", "REG-03", "REG-04"];

    [Fact]
    public void SeedFragmentsContainsAllRequiredRules()
    {
        var fragments = KnowledgeBaseSeedData.GetFragments();

        Assert.Equal(8, fragments.Count);

        foreach (var rule in RequiredRules)
        {
            var ruleFragments = fragments.Where(f => f.RuleId == rule).ToList();
            Assert.Equal(2, ruleFragments.Count);
        }
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
    public void SeedCausalRelationsCoversInterpretacionAndRecomendacionForEachRule()
    {
        var relations = KnowledgeBaseSeedData.GetCausalRelations();
        var fragments = KnowledgeBaseSeedData.GetFragments().ToDictionary(f => f.Id);

        Assert.Equal(8, relations.Count);

        var relationKeys = new HashSet<string>();

        foreach (var rule in RequiredRules)
        {
            var ruleRelations = relations.Where(r => r.RuleId == rule).ToList();
            Assert.Equal(2, ruleRelations.Count);

            Assert.Contains(ruleRelations, r => r.RelationType == CausalRelationType.Interpretacion);
            Assert.Contains(ruleRelations, r => r.RelationType == CausalRelationType.Recomendacion);
        }

        foreach (var relation in relations)
        {
            Assert.NotEqual(Guid.Empty, relation.Id);
            Assert.True(fragments.ContainsKey(relation.FragmentId), $"Fragmento asociado no existe: {relation.FragmentId}");

            var uniqueKey = $"{relation.RuleId}_{relation.FragmentId}_{relation.RelationType}";
            Assert.True(relationKeys.Add(uniqueKey), $"Relación causal duplicada detectada: {uniqueKey}");
        }
    }
}
