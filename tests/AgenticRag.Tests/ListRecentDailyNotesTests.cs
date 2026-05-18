using Xunit;

namespace AgenticRag.Tests;

[Collection(KnowledgeToolsCollection.Name)]
public sealed class ListRecentDailyNotesTests(KnowledgeToolsFixture fx)
{
    [Theory]
    [InlineData(0)]
    [InlineData(-7)]
    public async Task Rejects_nonpositive_lookback(int days)
    {
        var result = await fx.Tools.ListRecentDailyNotes(days);

        Assert.False(result.Ok);
        Assert.Contains("positive", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task Returns_distinct_notes_newest_first_within_window()
    {
        Skip.IfNot(fx.QdrantReachable, fx.SkipReason);

        const int days = 3650;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cutoff = today.AddDays(-days);

        var result = await fx.Tools.ListRecentDailyNotes(days);

        Assert.True(result.Ok, result.Error);
        var refs = result.Value!;

        var paths = refs.Select(r => r.Path).ToList();
        Assert.Equal(paths.Count, paths.Distinct().Count());

        Assert.All(refs, r =>
        {
            Assert.InRange(r.Date, cutoff, today);
            Assert.False(string.IsNullOrWhiteSpace(r.Path));
        });

        var dates = refs.Select(r => r.Date).ToList();
        Assert.Equal(dates.OrderByDescending(d => d).ToList(), dates);
    }

    [SkippableFact]
    public async Task Narrow_window_excludes_old_notes()
    {
        Skip.IfNot(fx.QdrantReachable, fx.SkipReason);

        var wide = await fx.Tools.ListRecentDailyNotes(3650);
        var narrow = await fx.Tools.ListRecentDailyNotes(1);

        Assert.True(wide.Ok && narrow.Ok);
        Assert.True(narrow.Value!.Count <= wide.Value!.Count);
    }
}
