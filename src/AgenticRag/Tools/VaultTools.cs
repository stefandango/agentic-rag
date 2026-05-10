using System.Globalization;
using AgenticRag.Configuration;
using AgenticRag.Embedding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using static Qdrant.Client.Grpc.Conditions;

namespace AgenticRag.Tools;

/// <summary>
/// v0.5 implementation of <see cref="IVaultTools"/>. Only <see cref="SearchVault"/> is wired —
/// the other four methods return <see cref="ToolResult{T}.Failure"/> with <c>"not implemented"</c>
/// so the agent loop can be developed against the full surface.
/// </summary>
/// <remarks>
/// Query vectors are produced by the same external embed pipeline that the indexer used,
/// so there is no model drift between index-time and query-time embeddings.
/// </remarks>
public sealed class VaultTools : IVaultTools
{
    private readonly AgenticRagOptions _options;
    private readonly ILogger<VaultTools> _logger;
    private readonly EmbedPipelineClient _embed;
    private readonly QdrantClient _qdrant;

    /// <summary>Construct with bound options, a logger, the embed pipeline client, and a Qdrant client.</summary>
    public VaultTools(
        IOptions<AgenticRagOptions> options,
        ILogger<VaultTools> logger,
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
    public async Task<ToolResult<IReadOnlyList<SearchHit>>> SearchVault(
        string query,
        int topK = 5,
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

        var filter = BuildFilter(tags, type, folders);

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

        var hits = points.Select(MapToSearchHit).ToList();
        return ToolResult<IReadOnlyList<SearchHit>>.Success(hits);
    }

    /// <inheritdoc />
    public Task<ToolResult<NoteContent>> GetNoteByPath(string path, CancellationToken ct = default)
        => Task.FromResult(ToolResult<NoteContent>.Failure("not implemented"));

    /// <inheritdoc />
    public Task<ToolResult<IReadOnlyList<SearchHit>>> SearchByTagOrType(
        string[]? tags, string? type, int limit = 20, CancellationToken ct = default)
        => Task.FromResult(ToolResult<IReadOnlyList<SearchHit>>.Failure("not implemented"));

    /// <inheritdoc />
    public Task<ToolResult<IReadOnlyList<DailyNoteRef>>> ListRecentDailyNotes(int days, CancellationToken ct = default)
        => Task.FromResult(ToolResult<IReadOnlyList<DailyNoteRef>>.Failure("not implemented"));

    /// <inheritdoc />
    public Task<ToolResult<IReadOnlyList<SearchHit>>> SearchKarakeep(string query, int topK = 5, CancellationToken ct = default)
        => Task.FromResult(ToolResult<IReadOnlyList<SearchHit>>.Failure("not implemented"));

    private static Filter? BuildFilter(string[]? tags, string? type, string[]? folders)
    {
        var conditions = new List<Condition>();

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

    private static SearchHit MapToSearchHit(ScoredPoint point)
    {
        var payload = point.Payload;

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
            Id: FormatPointId(point.Id),
            Source: SearchHitSource.Vault,
            Path: path,
            Title: title,
            Snippet: snippet,
            Score: point.Score,
            Metadata: meta);
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
