using System.Text.Json;
using System.Text.Json.Serialization;
using AgenticRag.Llm;
using AgenticRag.Tools;

namespace AgenticRag.Agent;

/// <summary>
/// The tool surface exposed to the model: JSON-Schema specs mirroring
/// <see cref="IKnowledgeTools"/>, plus a dispatcher that runs a call and
/// serialises the <see cref="ToolResult{T}"/> envelope back as a string.
/// </summary>
public static class ToolCatalog
{
    private static readonly JsonSerializerOptions Out = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Schemas handed to the LLM. Descriptions fold in the two known limitations.</summary>
    public static IReadOnlyList<ToolSpec> Specs { get; } =
    [
        Spec("search_knowledge",
            "Semantic search over the user's Obsidian vault. Use for questions that need " +
            "retrieving note content. Returns ranked hits (higher score = more relevant).",
            """
            {
              "type": "object",
              "properties": {
                "query": { "type": "string", "description": "Natural-language search query." },
                "top_k": { "type": "integer", "description": "Max hits to return (default 5)." },
                "tags": { "type": "array", "items": { "type": "string" }, "description": "Restrict to notes having ANY of these tags." },
                "type": { "type": "string", "description": "Restrict to a note_type: note, project, daily, blog, board." },
                "folders": { "type": "array", "items": { "type": "string" }, "description": "Restrict to notes under ANY of these vault folders." }
              },
              "required": ["query"]
            }
            """),
        Spec("get_note_by_path",
            "Fetch a full note by its vault-relative path. NOTE: the body and frontmatter are " +
            "reconstructed from the search index, not read from disk, so formatting and " +
            "non-indexed frontmatter keys may differ from the original file.",
            """
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Vault-relative path, e.g. 'projects/Self-Reliance Migration.md'." }
              },
              "required": ["path"]
            }
            """),
        Spec("search_by_tag_or_type",
            "Filter-only listing of notes by tag and/or type (no semantic query). At least one " +
            "of tags or type is required. Results are NOT relevance-ranked — score is always 0; " +
            "do not infer importance from order.",
            """
            {
              "type": "object",
              "properties": {
                "tags": { "type": "array", "items": { "type": "string" }, "description": "Match notes having ANY of these tags." },
                "type": { "type": "string", "description": "Exact note_type to match." },
                "limit": { "type": "integer", "description": "Max notes to return (default 20)." }
              },
              "required": []
            }
            """),
        Spec("list_recent_daily_notes",
            "List daily notes from the last N days, newest first.",
            """
            {
              "type": "object",
              "properties": {
                "days": { "type": "integer", "description": "Look-back window in days from today." }
              },
              "required": ["days"]
            }
            """),
    ];

    /// <summary>
    /// Execute one tool call. Never throws: bad JSON, unknown tool, or a failed
    /// <see cref="ToolResult{T}"/> all come back as a JSON envelope string for the model.
    /// </summary>
    public static async Task<string> DispatchAsync(
        IKnowledgeTools tools, string name, string argumentsJson, CancellationToken ct)
    {
        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            args = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return Envelope(false, null, $"could not parse tool arguments as JSON: {ex.Message}");
        }

        try
        {
            return name switch
            {
                "search_knowledge" => Project(await tools.SearchKnowledge(
                    Str(args, "query") ?? string.Empty,
                    Int(args, "top_k") ?? 5,
                    sources: null,
                    tags: Arr(args, "tags"),
                    type: Str(args, "type"),
                    folders: Arr(args, "folders"),
                    ct).ConfigureAwait(false)),

                "get_note_by_path" => Project(await tools.GetNoteByPath(
                    Str(args, "path") ?? string.Empty, ct).ConfigureAwait(false)),

                "search_by_tag_or_type" => Project(await tools.SearchByTagOrType(
                    Arr(args, "tags"), Str(args, "type"), Int(args, "limit") ?? 20, ct)
                    .ConfigureAwait(false)),

                "list_recent_daily_notes" => Project(await tools.ListRecentDailyNotes(
                    Int(args, "days") ?? 0, ct).ConfigureAwait(false)),

                _ => Envelope(false, null, $"unknown tool '{name}'"),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Envelope(false, null, $"tool '{name}' threw: {ex.Message}");
        }
    }

    private static ToolSpec Spec(string name, string description, string schema)
    {
        using var doc = JsonDocument.Parse(schema);
        return new ToolSpec(name, description, doc.RootElement.Clone());
    }

    private static string Project(ToolResult<IReadOnlyList<SearchHit>> r) =>
        r.Ok
            ? Envelope(true, r.Value!.Select(h => new
            {
                h.Id,
                source = h.Source.ToString(),
                h.Path,
                h.Title,
                h.Snippet,
                h.Score,
                note_type = h.Metadata.Vault?.NoteType,
                tags = h.Metadata.Tags,
                created = h.Metadata.Created?.ToString("yyyy-MM-dd"),
                updated = h.Metadata.Updated?.ToString("yyyy-MM-dd"),
            }), null)
            : Envelope(false, null, r.Error);

    private static string Project(ToolResult<NoteContent> r) =>
        r.Ok
            ? Envelope(true, new { r.Value!.Path, r.Value.Body, r.Value.Frontmatter }, null)
            : Envelope(false, null, r.Error);

    private static string Project(ToolResult<IReadOnlyList<DailyNoteRef>> r) =>
        r.Ok
            ? Envelope(true, r.Value!.Select(d => new
            {
                date = d.Date.ToString("yyyy-MM-dd"),
                d.Path,
                d.Summary,
            }), null)
            : Envelope(false, null, r.Error);

    private static string Envelope(bool ok, object? value, string? error) =>
        JsonSerializer.Serialize(new { ok, value, error }, Out);

    private static string? Str(JsonElement o, string key) =>
        o.ValueKind == JsonValueKind.Object
        && o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int? Int(JsonElement o, string key)
    {
        if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty(key, out var v))
        {
            return null;
        }
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(v.GetString(), out var n) => n,
            _ => null,
        };
    }

    private static string[]? Arr(JsonElement o, string key)
    {
        if (o.ValueKind != JsonValueKind.Object
            || !o.TryGetProperty(key, out var v)
            || v.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        var list = v.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!)
            .ToArray();
        return list.Length == 0 ? null : list;
    }
}
