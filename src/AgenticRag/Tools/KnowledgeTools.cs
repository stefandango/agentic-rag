using System.Globalization;
using System.Text;
using AgenticRag.Configuration;
using AgenticRag.Embedding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using static Qdrant.Client.Grpc.Conditions;

namespace AgenticRag.Tools;

/// <summary>
/// v0.5 implementation of <see cref="IKnowledgeTools"/>. All four read tools are wired against Qdrant.
/// </summary>
/// <remarks>
/// Query vectors are produced by the same external embed pipeline that the indexer used,
/// so there is no model drift between index-time and query-time embeddings.
/// <para>
/// There is no filesystem access to the vault — the indexed Qdrant copy is the only source.
/// <see cref="GetNoteByPath"/> therefore <em>reconstructs</em> a note from its chunks
/// (ordered by the payload <c>chunk_index</c>) and rebuilds frontmatter from payload fields
/// rather than the original YAML, so the result may differ slightly from the on-disk file.
/// </para>
/// </remarks>
public sealed class KnowledgeTools : IKnowledgeTools
{
    private readonly AgenticRagOptions _options;
    private readonly ILogger<KnowledgeTools> _logger;
    private readonly EmbedPipelineClient _embed;
    private readonly QdrantClient _qdrant;

    /// <summary>Construct with bound options, a logger, the embed pipeline client, and a Qdrant client.</summary>
    public KnowledgeTools(
        IOptions<AgenticRagOptions> options,
        ILogger<KnowledgeTools> logger,
        EmbedPipelineClient embed,
        QdrantClient qdrant)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(embed);
        ArgumentNullException.ThrowIfNull(qdrant);

        _options = options.Value;
        _logger = logger;
        _embed = embed;
        _qdrant = qdrant;
    }

    /// <inheritdoc />
    public async Task<ToolResult<IReadOnlyList<SearchHit>>> SearchKnowledge(
        string query,
        int topK = 5,
        string[]? sources = null,
        string[]? tags = null,
        string? type = null,
        string[]? folders = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolResult<IReadOnlyList<SearchHit>>.Failure("query must not be empty");
        }

        float[] vector;
        try
        {
            vector = await _embed.EmbedAsync(query, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "embed-pipeline call failed");
            return ToolResult<IReadOnlyList<SearchHit>>.Failure(
                $"embed service unreachable or invalid response: {ex.Message}");
        }

        var filter = BuildFilter(sources, tags, type, folders);

        IReadOnlyList<ScoredPoint> points;
        try
        {
            points = await _qdrant.SearchAsync(
                collectionName: _options.Qdrant.Collection,
                vector: vector,
                filter: filter,
                limit: (ulong)Math.Max(1, topK),
                vectorName: _options.Qdrant.VectorName,
                payloadSelector: true,
                cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "qdrant search failed against {Endpoint} collection {Collection}",
                _options.Qdrant.Endpoint, _options.Qdrant.Collection);
            return ToolResult<IReadOnlyList<SearchHit>>.Failure(
                $"vector search failed: {ex.Message}");
        }

        var hits = points.Select(p => MapToSearchHit(p, _logger)).ToList();
        return ToolResult<IReadOnlyList<SearchHit>>.Success(hits);
    }

    /// <inheritdoc />
    public async Task<ToolResult<NoteContent>> GetNoteByPath(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResult<NoteContent>.Failure("path must not be empty");
        }

        var filter = new Filter();
        filter.Must.Add(MatchKeyword("file", path));

        List<RetrievedPoint> points;
        try
        {
            points = await ScrollAllAsync(filter, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "qdrant scroll failed for note {Path}", path);
            return ToolResult<NoteContent>.Failure($"note lookup failed: {ex.Message}");
        }

        if (points.Count == 0)
        {
            return ToolResult<NoteContent>.Failure($"note not found: {path}");
        }

        var ordered = points
            .OrderBy(p => ReadLong(p.Payload, "chunk_index") ?? long.MaxValue)
            .ToList();

        // Reconstruct the body from chunk text. Section chunks carry their heading in a
        // separate payload field (stripped from `text`), so re-emit a `## heading` line
        // when the heading changes. Chunk 0 has an empty heading and its `text` already
        // contains the note's H1, so it is appended verbatim.
        var sb = new StringBuilder();
        string? lastHeading = null;
        foreach (var p in ordered)
        {
            var heading = ReadString(p.Payload, "heading");
            var text = ReadString(p.Payload, "text") ?? string.Empty;

            if (sb.Length > 0)
            {
                sb.Append("\n\n");
            }
            if (!string.IsNullOrWhiteSpace(heading) && heading != lastHeading)
            {
                sb.Append("## ").Append(heading).Append("\n\n");
            }
            sb.Append(text);
            lastHeading = heading;
        }

        var frontmatter = BuildFrontmatter(ordered[0].Payload);
        return ToolResult<NoteContent>.Success(new NoteContent(path, sb.ToString(), frontmatter));
    }

    /// <inheritdoc />
    public async Task<ToolResult<IReadOnlyList<SearchHit>>> SearchByTagOrType(
        string[]? tags, string? type, int limit = 20, CancellationToken ct = default)
    {
        var hasTags = tags is { Length: > 0 };
        var hasType = !string.IsNullOrWhiteSpace(type);
        if (!hasTags && !hasType)
        {
            return ToolResult<IReadOnlyList<SearchHit>>.Failure(
                "at least one of tags or type must be supplied");
        }

        var filter = BuildFilter(sources: null, tags: tags, type: type, folders: null);

        List<RetrievedPoint> points;
        try
        {
            points = await ScrollAllAsync(filter, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "qdrant scroll failed for tag/type filter");
            return ToolResult<IReadOnlyList<SearchHit>>.Failure(
                $"filter retrieval failed: {ex.Message}");
        }

        // Filter-only retrieval lists *notes*, not chunks: collapse to one hit per file,
        // keeping the lowest-chunk_index chunk (the note intro) as the representative.
        var hits = points
            .GroupBy(p => ReadString(p.Payload, "file") ?? string.Empty)
            .Select(g => g.OrderBy(p => ReadLong(p.Payload, "chunk_index") ?? long.MaxValue).First())
            .Select(p => MapPayloadToSearchHit(p.Id, p.Payload, score: 0f, _logger))
            .OrderBy(h => h.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, limit))
            .ToList();

        return ToolResult<IReadOnlyList<SearchHit>>.Success(hits);
    }

    /// <inheritdoc />
    public async Task<ToolResult<IReadOnlyList<DailyNoteRef>>> ListRecentDailyNotes(
        int days, CancellationToken ct = default)
    {
        if (days <= 0)
        {
            return ToolResult<IReadOnlyList<DailyNoteRef>>.Failure("days must be positive");
        }

        var filter = new Filter();
        filter.Must.Add(MatchKeyword("type", "daily"));

        List<RetrievedPoint> points;
        try
        {
            points = await ScrollAllAsync(filter, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "qdrant scroll failed for daily notes");
            return ToolResult<IReadOnlyList<DailyNoteRef>>.Failure(
                $"daily-note retrieval failed: {ex.Message}");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cutoff = today.AddDays(-days);

        var refs = points
            .GroupBy(p => ReadString(p.Payload, "file") ?? string.Empty)
            .Where(g => g.Key.Length > 0)
            .Select(g => g.First())
            .Select(p =>
            {
                var file = ReadString(p.Payload, "file")!;
                var date = ReadDate(p.Payload, "created")
                    ?? DailyDateFromTitleOrPath(ReadString(p.Payload, "title"), file);
                return (Date: date, Path: file);
            })
            .Where(t => t.Date is { } d && d >= cutoff && d <= today)
            .Select(t => new DailyNoteRef(t.Date!.Value, t.Path, Summary: null))
            .OrderByDescending(r => r.Date)
            .ToList();

        return ToolResult<IReadOnlyList<DailyNoteRef>>.Success(refs);
    }

    /// <summary>
    /// Page through every point matching <paramref name="filter"/> (payload only, no vectors).
    /// Filter-only tools have no natural top-K, so all matches are materialised.
    /// </summary>
    private async Task<List<RetrievedPoint>> ScrollAllAsync(Filter? filter, CancellationToken ct)
    {
        const uint pageSize = 256;
        var all = new List<RetrievedPoint>();
        PointId? offset = null;

        while (true)
        {
            var page = await _qdrant.ScrollAsync(
                _options.Qdrant.Collection,
                filter: filter,
                limit: pageSize,
                offset: offset,
                payloadSelector: true,
                vectorsSelector: false,
                cancellationToken: ct).ConfigureAwait(false);

            all.AddRange(page.Result);

            if (page.NextPageOffset is null)
            {
                break;
            }
            offset = page.NextPageOffset;
        }

        return all;
    }

    /// <summary>
    /// Rebuild a frontmatter-shaped map from the note-level payload fields the indexer wrote.
    /// This is a reconstruction, not the original YAML — only indexed keys are present.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> BuildFrontmatter(
        Google.Protobuf.Collections.MapField<string, Value> payload)
    {
        var fm = new Dictionary<string, object?>();

        foreach (var key in new[] { "title", "type", "status", "created", "updated", "folder" })
        {
            var value = ReadString(payload, key);
            if (!string.IsNullOrEmpty(value))
            {
                fm[key] = value;
            }
        }

        var tags = ReadStringList(payload, "tags");
        if (tags.Count > 0)
        {
            fm["tags"] = tags;
        }

        var folders = ReadStringList(payload, "folders");
        if (folders.Count > 0)
        {
            fm["folders"] = folders;
        }

        return fm;
    }

    private static readonly System.Text.RegularExpressions.Regex IsoDate =
        new(@"\d{4}-\d{2}-\d{2}", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static DateOnly? DailyDateFromTitleOrPath(string? title, string path)
    {
        foreach (var candidate in new[] { title, Path.GetFileNameWithoutExtension(path) })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }
            var m = IsoDate.Match(candidate);
            if (m.Success && DateOnly.TryParseExact(
                m.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                return d;
            }
        }
        return null;
    }

    private static Filter? BuildFilter(string[]? sources, string[]? tags, string? type, string[]? folders)
    {
        var conditions = new List<Condition>();

        if (sources is { Length: > 0 })
        {
            conditions.Add(MatchAnyKeyword("source", sources));
        }

        if (tags is { Length: > 0 })
        {
            conditions.Add(MatchAnyKeyword("tags", tags));
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            conditions.Add(MatchKeyword("type", type));
        }

        if (folders is { Length: > 0 })
        {
            conditions.Add(MatchAnyKeyword("folders", folders));
        }

        if (conditions.Count == 0)
        {
            return null;
        }

        var filter = new Filter();
        filter.Must.AddRange(conditions);
        return filter;
    }

    private static Condition MatchAnyKeyword(string field, IEnumerable<string> values)
    {
        var keywords = new RepeatedStrings();
        keywords.Strings.AddRange(values);
        return new Condition
        {
            Field = new FieldCondition
            {
                Key = field,
                Match = new Match { Keywords = keywords },
            },
        };
    }

    private static SearchHit MapToSearchHit(ScoredPoint point, ILogger logger)
        => MapPayloadToSearchHit(point.Id, point.Payload, point.Score, logger);

    private static SearchHit MapPayloadToSearchHit(
        PointId id,
        Google.Protobuf.Collections.MapField<string, Value> payload,
        float score,
        ILogger logger)
    {
        // Payload keys verified by scrolling a point: the indexer writes `file` (not `path`)
        // and `title` (not `note_title`). `status` is optional and absent on most points.
        var path = ReadString(payload, "file") ?? string.Empty;
        var heading = ReadString(payload, "heading");
        var noteTitle = ReadString(payload, "title");
        var noteType = ReadString(payload, "type") ?? "unknown";
        var status = ReadString(payload, "status");
        var snippet = ReadString(payload, "text") ?? string.Empty;

        var tags = ReadStringList(payload, "tags");
        var folders = ReadStringList(payload, "folders");
        var created = ReadDate(payload, "created");
        var updated = ReadDate(payload, "updated");

        var title = FirstNonEmpty(heading, noteTitle) ?? DeriveTitleFromPath(path);

        var source = MapSource(ReadString(payload, "source"), logger);

        var vault = new VaultMetadata(
            Heading: heading,
            NoteType: noteType,
            Folders: folders,
            Status: status,
            NoteTitle: noteTitle);

        var meta = new HitMetadata(
            Created: created,
            Updated: updated,
            Tags: tags,
            Vault: vault,
            Karakeep: null);

        return new SearchHit(
            Id: FormatPointId(id),
            Source: source,
            Path: path,
            Title: title,
            Snippet: snippet,
            Score: score,
            Metadata: meta);
    }

    private static SearchHitSource MapSource(string? raw, ILogger logger)
    {
        if (string.IsNullOrEmpty(raw))
        {
            // v0.5 only indexes the vault; older points predate the `source` field.
            return SearchHitSource.Vault;
        }
        if (string.Equals(raw, "vault", StringComparison.OrdinalIgnoreCase))
        {
            return SearchHitSource.Vault;
        }
        if (string.Equals(raw, "karakeep", StringComparison.OrdinalIgnoreCase))
        {
            return SearchHitSource.Karakeep;
        }
        logger.LogWarning("unknown payload source {Source}; treating as vault", raw);
        return SearchHitSource.Vault;
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c))
            {
                return c;
            }
        }
        return null;
    }

    private static string DeriveTitleFromPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "(untitled)";
        }
        var name = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private static string FormatPointId(PointId id) => id.PointIdOptionsCase switch
    {
        PointId.PointIdOptionsOneofCase.Uuid => id.Uuid,
        PointId.PointIdOptionsOneofCase.Num => id.Num.ToString(CultureInfo.InvariantCulture),
        _ => id.ToString(),
    };

    private static string? ReadString(Google.Protobuf.Collections.MapField<string, Value> payload, string key)
    {
        if (!payload.TryGetValue(key, out var v) || v is null)
        {
            return null;
        }
        return v.KindCase switch
        {
            Value.KindOneofCase.StringValue => v.StringValue,
            Value.KindOneofCase.NullValue => null,
            _ => null,
        };
    }

    private static IReadOnlyList<string> ReadStringList(Google.Protobuf.Collections.MapField<string, Value> payload, string key)
    {
        if (!payload.TryGetValue(key, out var v) || v is null || v.KindCase != Value.KindOneofCase.ListValue)
        {
            return [];
        }
        var list = new List<string>(v.ListValue.Values.Count);
        foreach (var item in v.ListValue.Values)
        {
            if (item.KindCase == Value.KindOneofCase.StringValue)
            {
                list.Add(item.StringValue);
            }
        }
        return list;
    }

    private static long? ReadLong(Google.Protobuf.Collections.MapField<string, Value> payload, string key)
    {
        if (!payload.TryGetValue(key, out var v) || v is null)
        {
            return null;
        }
        return v.KindCase switch
        {
            Value.KindOneofCase.IntegerValue => v.IntegerValue,
            Value.KindOneofCase.DoubleValue => (long)v.DoubleValue,
            _ => null,
        };
    }

    private static DateOnly? ReadDate(Google.Protobuf.Collections.MapField<string, Value> payload, string key)
    {
        var s = ReadString(payload, key);
        if (string.IsNullOrEmpty(s))
        {
            return null;
        }
        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            return d;
        }
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
        {
            return DateOnly.FromDateTime(dt);
        }
        return null;
    }
}
