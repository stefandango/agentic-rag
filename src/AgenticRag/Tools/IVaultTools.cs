namespace AgenticRag.Tools;

/// <summary>
/// The full set of tools the agent loop can call. Every method returns a
/// <see cref="ToolResult{T}"/> so failures stay in the control-flow layer
/// and never escape as exceptions across the tool boundary.
/// </summary>
/// <remarks>
/// v0.5 surface is read-only: search, fetch, list. Write/effect tools land in v1
/// and their signatures are sketched as comments at the bottom of this file so the
/// result-shape conventions are visible while the read surface stabilises.
/// </remarks>
public interface IVaultTools
{
    /// <summary>
    /// Semantic search over the indexed Obsidian vault.
    /// </summary>
    /// <param name="query">Natural-language query — embedded with the same model as the indexer.</param>
    /// <param name="topK">Maximum number of hits to return.</param>
    /// <param name="tags">If supplied, restrict to notes whose tag list contains <em>any</em> of these (OR / Qdrant <c>MatchAny</c>).</param>
    /// <param name="type">If supplied, restrict to notes with this <c>note_type</c> (exact match — e.g. <c>note</c>, <c>project</c>, <c>daily</c>, <c>blog</c>, <c>board</c>).</param>
    /// <param name="folders">If supplied, restrict to notes whose ancestor-folder list contains <em>any</em> of these (OR / <c>MatchAny</c>). Pass parent paths like <c>"projects"</c> or <c>"projects/Self-Reliance Migration"</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of hits (highest score first), or a failure envelope.</returns>
    Task<ToolResult<IReadOnlyList<SearchHit>>> SearchVault(
        string query,
        int topK = 5,
        string[]? tags = null,
        string? type = null,
        string[]? folders = null,
        CancellationToken ct = default);

    /// <summary>
    /// Fetch a full note by its vault-relative path.
    /// </summary>
    /// <param name="path">Vault-relative path, e.g. <c>"projects/Self-Reliance Migration.md"</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Note body and parsed frontmatter, or a failure envelope.</returns>
    Task<ToolResult<NoteContent>> GetNoteByPath(string path, CancellationToken ct = default);

    /// <summary>
    /// Filter-only retrieval: list notes by tag and/or type without a semantic query.
    /// At least one of <paramref name="tags"/> or <paramref name="type"/> must be non-null/non-empty;
    /// implementations should return <see cref="ToolResult{T}.Failure"/> otherwise.
    /// </summary>
    /// <param name="tags">Tags to match (OR / <c>MatchAny</c>) against each note's tag list.</param>
    /// <param name="type">Exact <c>note_type</c> to match.</param>
    /// <param name="limit">Maximum number of hits to return.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ToolResult<IReadOnlyList<SearchHit>>> SearchByTagOrType(
        string[]? tags,
        string? type,
        int limit = 20,
        CancellationToken ct = default);

    /// <summary>
    /// List recent daily notes (notes whose <c>note_type</c> is <c>daily</c>), newest first.
    /// </summary>
    /// <param name="days">Look-back window in days from today.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ToolResult<IReadOnlyList<DailyNoteRef>>> ListRecentDailyNotes(int days, CancellationToken ct = default);

    /// <summary>
    /// Semantic search over Karakeep bookmarks.
    /// </summary>
    /// <param name="query">Natural-language query.</param>
    /// <param name="topK">Maximum number of hits to return.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ToolResult<IReadOnlyList<SearchHit>>> SearchKarakeep(
        string query,
        int topK = 5,
        CancellationToken ct = default);

    // ---------------------------------------------------------------------
    // v1 write-tool sketches — signatures only. Same ToolResult<T> envelope;
    // effect-only tools use ToolResult<Unit>.
    //
    // /// <summary>Append a structured annotation to an inbox note.</summary>
    // Task<ToolResult<Unit>> AnnotateInboxNote(
    //     string path, string annotation, CancellationToken ct = default);
    //
    // /// <summary>Append a section to today's daily note (creating it if missing).</summary>
    // Task<ToolResult<Unit>> AppendToDailyNote(
    //     string section, string body, CancellationToken ct = default);
    //
    // /// <summary>Create a new brief note from a template + populated fields.</summary>
    // Task<ToolResult<NoteContent>> CreateBrief(
    //     string title, IReadOnlyDictionary<string, object?> fields, CancellationToken ct = default);
    //
    // /// <summary>Propose archiving a note — returns a draft action for the user to confirm.</summary>
    // Task<ToolResult<Unit>> ProposeArchive(
    //     string path, string reason, CancellationToken ct = default);
    // ---------------------------------------------------------------------
}

/// <summary>
/// Full content of a single vault note as returned by <see cref="IVaultTools.GetNoteByPath"/>.
/// </summary>
/// <param name="Path">Vault-relative path of the note.</param>
/// <param name="Body">Markdown body with frontmatter stripped.</param>
/// <param name="Frontmatter">Parsed YAML frontmatter as a key/value map. Empty if the note has none.</param>
public record NoteContent(
    string Path,
    string Body,
    IReadOnlyDictionary<string, object?> Frontmatter);

/// <summary>
/// A pointer to a daily note returned by <see cref="IVaultTools.ListRecentDailyNotes"/>.
/// </summary>
/// <param name="Date">The calendar date the daily note belongs to.</param>
/// <param name="Path">Vault-relative path to the note file.</param>
/// <param name="Summary">Short summary if the note carries one in frontmatter, otherwise null.</param>
public record DailyNoteRef(DateOnly Date, string Path, string? Summary);
