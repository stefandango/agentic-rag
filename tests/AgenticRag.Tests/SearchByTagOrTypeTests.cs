using Xunit;

namespace AgenticRag.Tests;

[Collection(KnowledgeToolsCollection.Name)]
public sealed class SearchByTagOrTypeTests(KnowledgeToolsFixture fx)
{
    [Fact]
    public async Task Requires_at_least_one_of_tags_or_type()
    {
        var result = await fx.Tools.SearchByTagOrType(tags: null, type: null);

        Assert.False(result.Ok);
        Assert.Null(result.Value);
        Assert.Contains("tags or type", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Empty_tags_array_is_treated_as_absent()
    {
        var result = await fx.Tools.SearchByTagOrType(tags: [], type: "   ");

        Assert.False(result.Ok);
    }

    [SkippableFact]
    public async Task Type_filter_returns_one_hit_per_note_all_of_that_type()
    {
        Skip.IfNot(fx.QdrantReachable, fx.SkipReason);

        var result = await fx.Tools.SearchByTagOrType(tags: null, type: "daily", limit: 50);

        Assert.True(result.Ok, result.Error);
        Assert.NotNull(result.Value);

        var paths = result.Value!.Select(h => h.Path).ToList();
        Assert.Equal(paths.Count, paths.Distinct().Count()); // collapsed to one hit per note
        Assert.All(result.Value, h =>
            Assert.Equal("daily", h.Metadata.Vault?.NoteType, ignoreCase: true));
    }

    [SkippableFact]
    public async Task Limit_is_honoured()
    {
        Skip.IfNot(fx.QdrantReachable, fx.SkipReason);

        var result = await fx.Tools.SearchByTagOrType(tags: null, type: "note", limit: 3);

        Assert.True(result.Ok, result.Error);
        Assert.True(result.Value!.Count <= 3);
    }
}
