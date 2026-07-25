using NodaTime;
using TimeCalculation.Api.Validation;
using Xunit;

namespace TimeCalculation.Api.Tests;

/// <summary>
/// The overlap rule on its own — no database, no HTTP. These are the cases that are easy to get
/// wrong by an off-by-one day and hard to notice afterwards, because a subtly wrong rule still looks
/// right for the common "clearly separate" and "clearly identical" ranges.
/// </summary>
public class DateRangeTests
{
    private static DateRange Range(string from, string? to) =>
        new(LocalDate.FromDateOnly(DateOnly.Parse(from)),
            to is null ? null : LocalDate.FromDateOnly(DateOnly.Parse(to)));

    [Theory]
    // Clearly disjoint, in both directions.
    [InlineData("2026-01-01", "2026-01-31", "2026-02-01", "2026-02-28", false)]
    [InlineData("2026-02-01", "2026-02-28", "2026-01-01", "2026-01-31", false)]
    // Touching at the boundary: end date is inclusive, so sharing a single day IS an overlap. This
    // is the off-by-one that a naive `<` instead of `<=` gets wrong. BOTH directions are listed
    // deliberately — Overlaps compares two conditions, and each direction exercises a different one.
    // With only the first case present, changing `From <= other.To` to `From <` still passed here
    // (the other condition caught it) and the gap only showed up in an integration test.
    [InlineData("2026-01-01", "2026-01-31", "2026-01-31", "2026-02-28", true)]
    [InlineData("2026-01-31", "2026-02-28", "2026-01-01", "2026-01-31", true)]
    // Adjacent with no gap and no shared day — the correct way to succeed one assignment with
    // another.
    [InlineData("2026-01-01", "2026-01-30", "2026-01-31", "2026-02-28", false)]
    // Wholly contained.
    [InlineData("2026-01-01", "2026-12-31", "2026-06-01", "2026-06-30", true)]
    // Identical.
    [InlineData("2026-01-01", "2026-01-31", "2026-01-01", "2026-01-31", true)]
    public void ClosedRanges(string aFrom, string aTo, string bFrom, string bTo, bool expected)
    {
        Assert.Equal(expected, Range(aFrom, aTo).Overlaps(Range(bFrom, bTo)));
    }

    [Fact]
    public void OpenEndedRange_OverlapsAnythingStartingAfterIt()
    {
        var openEnded = Range("2026-01-01", null);
        Assert.True(openEnded.Overlaps(Range("2030-01-01", "2030-12-31")));
        Assert.True(openEnded.Overlaps(Range("2026-01-01", null)));
    }

    [Fact]
    public void OpenEndedRange_DoesNotOverlapAnythingEndingBeforeItBegins()
    {
        var openEnded = Range("2026-06-01", null);
        Assert.False(openEnded.Overlaps(Range("2026-01-01", "2026-05-31")));
    }

    [Fact]
    public void Overlaps_IsSymmetric()
    {
        // Order of comparison must not change the answer — the service compares a proposed range
        // against existing ones in whatever order the database returns them.
        var a = Range("2026-01-01", null);
        var b = Range("2026-03-01", "2026-04-01");
        Assert.Equal(a.Overlaps(b), b.Overlaps(a));
    }

    [Theory]
    [InlineData("2026-01-01", "2026-01-01", true)]   // single day
    [InlineData("2026-01-01", "2025-12-31", false)]  // ends before it starts
    [InlineData("2026-01-01", null, true)]           // open-ended
    public void IsWellFormed(string from, string? to, bool expected)
    {
        Assert.Equal(expected, Range(from, to).IsWellFormed);
    }
}
