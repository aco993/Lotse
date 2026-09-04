using Lotse.Core.Engine;
using Lotse.Core.Model;

namespace Lotse.Core.Tests;

/// <summary>The three cards on Fortschritt, built from hand-made FSRS states so every number is checkable.</summary>
public class ProgressStatsTests
{
    private static readonly DateTime Now = new(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);

    private static ReviewState Item(string id, double stability, double dueInDays, double lastReviewDaysAgo)
        => new()
        {
            ExerciseId = id,
            NodeId = "GR.PASSIV",
            Stability = stability,
            Repetitions = 1,
            DueUtc = Now.AddDays(dueInDays),
            LastReviewUtc = Now.AddDays(-lastReviewDaysAgo),
        };

    [Fact]
    public void Nothing_scheduled_yields_zeros_instead_of_a_division_by_zero()
    {
        var stats = ProgressStatsCalculator.Compute([], Now);
        Assert.Equal(0, stats.Stuck);
        Assert.Equal(0, stats.Scheduled);
        Assert.Equal(0, stats.AverageRetrievability);
        Assert.Equal(7, stats.DueNext7Days.Count);
        Assert.All(stats.DueNext7Days, d => Assert.Equal(0, d.Count));
    }

    [Fact]
    public void Only_items_past_the_threshold_count_as_stuck()
    {
        var stats = ProgressStatsCalculator.Compute(
        [
            Item("a", stability: 20.9, dueInDays: 5, lastReviewDaysAgo: 1),   // just under
            Item("b", stability: 21.0, dueInDays: 5, lastReviewDaysAgo: 1),   // exactly on it
            Item("c", stability: 400, dueInDays: 5, lastReviewDaysAgo: 30),   // long past, reviewed a month ago
        ], Now);

        Assert.Equal(2, stats.Stuck);
        // "b" was reviewed yesterday, "c" a month ago.
        Assert.Equal(1, stats.StuckReviewedThisWeek);
    }

    [Fact]
    public void A_new_item_is_not_scheduled_and_does_not_dilute_retention()
    {
        var fresh = new ReviewState { ExerciseId = "neu", NodeId = "GR.PASSIV", DueUtc = Now };
        var stats = ProgressStatsCalculator.Compute([fresh, Item("a", 50, 3, 1)], Now);

        Assert.Equal(1, stats.Scheduled);
        Assert.True(stats.AverageRetrievability > 0.9, $"war {stats.AverageRetrievability}");
    }

    [Fact]
    public void Overdue_items_are_shown_on_today_not_hidden_in_the_past()
    {
        var stats = ProgressStatsCalculator.Compute(
        [
            Item("alt", 30, dueInDays: -9, lastReviewDaysAgo: 40),
            Item("gestern", 30, dueInDays: -1, lastReviewDaysAgo: 10),
            Item("heute", 30, dueInDays: 0, lastReviewDaysAgo: 10),
            Item("in3", 30, dueInDays: 3, lastReviewDaysAgo: 2),
            Item("in9", 30, dueInDays: 9, lastReviewDaysAgo: 2),   // beyond the window
        ], Now);

        Assert.Equal(3, stats.DueNext7Days[0].Count);   // two overdue plus today's
        Assert.Equal(1, stats.DueNext7Days[3].Count);
        Assert.Equal(4, stats.DueNext7Days.Sum(d => d.Count));   // "in9" is outside the seven days
        Assert.Equal(DateOnly.FromDateTime(Now), stats.DueNext7Days[0].Day);
    }

    [Fact]
    public void Retention_falls_as_an_item_ages_past_its_stability()
    {
        var frisch = ProgressStatsCalculator.Compute([Item("a", 10, 5, lastReviewDaysAgo: 0)], Now).AverageRetrievability;
        var alt = ProgressStatsCalculator.Compute([Item("a", 10, 5, lastReviewDaysAgo: 30)], Now).AverageRetrievability;
        Assert.True(frisch > alt, $"frisch {frisch} müsste über alt {alt} liegen");
    }
}
