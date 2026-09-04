using Lotse.Core.Model;

namespace Lotse.Core.Engine;

public sealed record DueDay(DateOnly Day, int Count);

/// <summary>
/// The numbers behind the three cards on Fortschritt. All of them read the FSRS state that already exists - nothing
/// is tracked extra, and nothing is invented.
/// </summary>
/// <param name="Stuck">Items whose memory stability has passed <see cref="ProgressStatsCalculator.StuckDays"/>.</param>
/// <param name="StuckReviewedThisWeek">
/// Of those, the ones reviewed in the last seven days. Deliberately NOT "newly stuck this week": no history of past
/// stabilities is stored, so the moment an item crossed the line cannot be known, and a number that looks precise
/// while being guessed is worse than a slightly duller one that is true.
/// </param>
/// <param name="Scheduled">Items with a schedule at all - the denominator for <paramref name="AverageRetrievability"/>.</param>
/// <param name="DueNext7Days">Due counts per day, starting today; always seven entries, zeros included.</param>
/// <param name="AverageRetrievability">Mean current recall probability over the scheduled items; 0 when there are none.</param>
public sealed record ProgressStats(
    int Stuck,
    int StuckReviewedThisWeek,
    int Scheduled,
    IReadOnlyList<DueDay> DueNext7Days,
    double AverageRetrievability);

public static class ProgressStatsCalculator
{
    /// <summary>Three weeks: long enough that an item survives a normal gap in practice, short enough to be reachable.</summary>
    public const double StuckDays = 21;

    public static ProgressStats Compute(IReadOnlyCollection<ReviewState> reviews, DateTime nowUtc)
    {
        var scheduled = reviews.Where(r => !r.IsNew && r.LastReviewUtc is not null).ToList();
        var weekAgo = nowUtc.AddDays(-7);

        var stuck = scheduled.Where(r => r.Stability >= StuckDays).ToList();
        var today = DateOnly.FromDateTime(nowUtc);
        var dueByDay = Enumerable.Range(0, 7)
            .Select(offset =>
            {
                var day = today.AddDays(offset);
                // Everything overdue counts on today, otherwise a backlog would be invisible on the chart.
                var count = scheduled.Count(r => offset == 0
                    ? DateOnly.FromDateTime(r.DueUtc) <= day
                    : DateOnly.FromDateTime(r.DueUtc) == day);
                return new DueDay(day, count);
            })
            .ToList();

        return new ProgressStats(
            stuck.Count,
            stuck.Count(r => r.LastReviewUtc >= weekAgo),
            scheduled.Count,
            dueByDay,
            scheduled.Count == 0 ? 0 : scheduled.Average(r => r.RetrievabilityAt(nowUtc)));
    }
}
