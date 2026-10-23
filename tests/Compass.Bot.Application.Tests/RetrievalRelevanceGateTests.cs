using Compass.Agents.Contracts.Retrieval;
using Compass.Bot.Application.Models;

namespace Compass.Bot.Application.Tests;

/// <summary>
/// The gate is a relevance cut, not a permission filter: it can only remove hits the store already returned, never add
/// one, and never reorders.
/// </summary>
public sealed class RetrievalRelevanceGateTests
{
    private static RetrievalHit Hit(string name, double? score) => new($"https://contoso.sharepoint.com/{name}", name, $"text of {name}", score);

    [Fact]
    public void Hits_below_the_minimum_are_dropped()
    {
        var result = RetrievalRelevanceGate.Apply(new RetrievalResult([Hit("weak", 0.58), Hit("strong", 0.81)]), 0.7);

        Assert.Equal(["strong"], result.Hits.Select(h => h.Title));
    }

    [Fact]
    public void A_hit_exactly_at_the_minimum_is_kept()
    {
        var result = RetrievalRelevanceGate.Apply(new RetrievalResult([Hit("edge", 0.7)]), 0.7);

        Assert.Single(result.Hits);
    }

    [Fact]
    public void A_hit_above_the_minimum_is_kept()
    {
        var result = RetrievalRelevanceGate.Apply(new RetrievalResult([Hit("above", 0.9)]), 0.7);

        Assert.Single(result.Hits);
    }

    [Fact]
    public void A_hit_with_no_score_is_kept_because_a_missing_number_is_not_a_relevance_verdict()
    {
        var result = RetrievalRelevanceGate.Apply(new RetrievalResult([Hit("unscored", null), Hit("weak", 0.1)]), 0.7);

        Assert.Equal(["unscored"], result.Hits.Select(h => h.Title));
    }

    [Fact]
    public void The_order_the_api_returned_is_kept_even_when_it_is_not_by_score()
    {
        var result = RetrievalRelevanceGate.Apply(
            new RetrievalResult([Hit("b", 0.75), Hit("drop", 0.2), Hit("a", 0.95), Hit("none", null), Hit("c", 0.7)]),
            0.7);

        Assert.Equal(["b", "a", "none", "c"], result.Hits.Select(h => h.Title));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_minimum_of_zero_or_less_keeps_everything_including_the_lowest_scores(double minimum)
    {
        var input = new RetrievalResult([Hit("low", 0.0), Hit("none", null), Hit("high", 1.0)]);

        Assert.Equal(input.Hits.Select(h => h.Title), RetrievalRelevanceGate.Apply(input, minimum).Hits.Select(h => h.Title));
    }

    [Fact]
    public void When_every_hit_is_weak_nothing_is_left()
    {
        var result = RetrievalRelevanceGate.Apply(new RetrievalResult([Hit("a", 0.2), Hit("b", 0.3)]), 0.9);

        Assert.Empty(result.Hits);
    }

    [Fact]
    public void The_gate_never_adds_or_changes_a_hit()
    {
        var input = new RetrievalResult([Hit("a", 0.8), Hit("b", 0.2)]);

        var result = RetrievalRelevanceGate.Apply(input, 0.5);

        Assert.All(result.Hits, hit => Assert.Contains(hit, input.Hits));
        Assert.Equal(2, input.Hits.Count);
    }
}
