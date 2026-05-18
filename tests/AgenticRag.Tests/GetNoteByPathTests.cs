using Xunit;

namespace AgenticRag.Tests;

[Collection(KnowledgeToolsCollection.Name)]
public sealed class GetNoteByPathTests(KnowledgeToolsFixture fx)
{
    [Fact]
    public async Task Empty_path_fails_without_touching_qdrant()
    {
        var result = await fx.Tools.GetNoteByPath("   ");

        Assert.False(result.Ok);
        Assert.Null(result.Value);
        Assert.Contains("path", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task Missing_note_returns_not_found()
    {
        Skip.IfNot(fx.QdrantReachable, fx.SkipReason);

        var result = await fx.Tools.GetNoteByPath("does/not/exist-" + Guid.NewGuid() + ".md");

        Assert.False(result.Ok);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task Reconstructs_body_and_frontmatter_for_a_real_note()
    {
        Skip.IfNot(fx.QdrantReachable, fx.SkipReason);

        // Discover a real path rather than hardcoding vault contents.
        var listed = await fx.Tools.SearchByTagOrType(tags: null, type: "note", limit: 1);
        Skip.If(listed.Value is not { Count: > 0 }, "No 'note'-typed notes indexed to test against.");
        var path = listed.Value![0].Path;

        var result = await fx.Tools.GetNoteByPath(path);

        Assert.True(result.Ok, result.Error);
        Assert.NotNull(result.Value);
        Assert.Equal(path, result.Value!.Path);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.Body));
        // Frontmatter is reconstructed from payload; "type" is always written by the indexer.
        Assert.True(result.Value.Frontmatter.ContainsKey("type"));
    }
}
