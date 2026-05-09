namespace AgenticRag.Tools;

/// <summary>
/// A single retrieval result returned by any search tool. The shape is
/// uniform across vault and Karakeep so the agent loop can reason over a
/// flat list regardless of where each hit came from.
/// </summary>
/// <param name="Id">Stable identifier within <paramref name="Source"/> (Qdrant point id, Karakeep bookmark id, etc.).</param>
/// <param name="Source">Origin of the hit — vault chunk or Karakeep bookmark.</param>
/// <param name="Path">Vault-relative path for vault hits; canonical URL for Karakeep hits.</param>
/// <param name="Title">Display title. Never null — implementations must apply the title fallback chain.</param>
/// <param name="Snippet">Body text as the indexer wrote it. Not truncated further by the search tool.</param>
/// <param name="Score">Similarity score from the underlying index (higher is better).</param>
/// <param name="Metadata">Source-specific metadata; exactly one of <see cref="HitMetadata.Vault"/> or <see cref="HitMetadata.Karakeep"/> is non-null.</param>
public record SearchHit(
    string Id,
    SearchHitSource Source,
    string Path,
    string Title,
    string Snippet,
    float Score,
    HitMetadata Metadata);

/// <summary>Origin of a <see cref="SearchHit"/>.</summary>
public enum SearchHitSource
{
    /// <summary>Hit came from the Obsidian vault Qdrant collection.</summary>
    Vault,
    /// <summary>Hit came from Karakeep bookmarks.</summary>
    Karakeep,
}

/// <summary>
/// Metadata common to all hits, plus a discriminated payload for the source-specific bits.
/// </summary>
/// <param name="Created">Original creation date if known.</param>
/// <param name="Updated">Last update date if known.</param>
/// <param name="Tags">Tags attached to the underlying note or bookmark. Empty list if none.</param>
/// <param name="Vault">Populated when <see cref="SearchHit.Source"/> is <see cref="SearchHitSource.Vault"/>.</param>
/// <param name="Karakeep">Populated when <see cref="SearchHit.Source"/> is <see cref="SearchHitSource.Karakeep"/>.</param>
public record HitMetadata(
    DateOnly? Created,
    DateOnly? Updated,
    IReadOnlyList<string> Tags,
    VaultMetadata? Vault,
    KarakeepMetadata? Karakeep);

/// <summary>
/// Vault-specific metadata mirrored from the Qdrant payload.
/// </summary>
/// <param name="Heading">Section heading (e.g. "## Goals") when the chunk corresponds to a section. Null when the chunk covers the whole note (no <c>##</c> sections).</param>
/// <param name="NoteType">Note type from the payload — kept stringly-typed at the boundary to avoid churn when new types are added (current values: <c>note</c>, <c>project</c>, <c>daily</c>, <c>blog</c>, <c>board</c>).</param>
/// <param name="Folders">Every directory ancestor of the note (e.g. <c>["projects", "projects/Self-Reliance Migration"]</c>). Mirrors the Qdrant payload <c>folders</c> array. Folder filters use OR / <c>MatchAny</c> semantics against this list.</param>
/// <param name="Status">Frontmatter status if present (e.g. <c>active</c>, <c>archived</c>).</param>
/// <param name="NoteTitle">Frontmatter or first-h1 note title. May differ from <see cref="SearchHit.Title"/> when a heading is preferred.</param>
public record VaultMetadata(
    string? Heading,
    string NoteType,
    IReadOnlyList<string> Folders,
    string? Status,
    string? NoteTitle);

/// <summary>
/// Karakeep-specific metadata.
/// </summary>
/// <param name="Url">Canonical URL of the bookmarked page.</param>
/// <param name="Domain">Host portion of <paramref name="Url"/> for grouping/display.</param>
/// <param name="Author">Author/byline if Karakeep extracted one.</param>
/// <param name="ArchivedAt">Date the bookmark was archived, if archived.</param>
/// <param name="IsArchived">Archive flag from Karakeep, when known.</param>
public record KarakeepMetadata(
    string Url,
    string? Domain,
    string? Author,
    DateOnly? ArchivedAt,
    bool? IsArchived);

/// <summary>
/// Result envelope for every tool call. Tool failures are returned as values, not thrown,
/// so the agent loop can treat them as control flow rather than exception unwinding.
/// </summary>
/// <typeparam name="T">Payload type on success.</typeparam>
/// <param name="Ok">True iff the call succeeded and <paramref name="Value"/> is meaningful.</param>
/// <param name="Value">Result payload when <paramref name="Ok"/> is true. Default/null otherwise.</param>
/// <param name="Error">Human-readable error message when <paramref name="Ok"/> is false. Null on success.</param>
public record ToolResult<T>(bool Ok, T? Value, string? Error)
{
    /// <summary>Wrap a successful payload.</summary>
    public static ToolResult<T> Success(T value) => new(true, value, null);

    /// <summary>Wrap a failure with a human-readable message.</summary>
    public static ToolResult<T> Failure(string error) => new(false, default, error);
}

/// <summary>
/// Marker type for tool results that have no payload — used by write/effect tools where
/// the absence of an error is the meaningful signal.
/// </summary>
public sealed record Unit
{
    /// <summary>The single <see cref="Unit"/> instance.</summary>
    public static Unit Value { get; } = new();
}
