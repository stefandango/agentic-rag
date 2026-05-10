namespace AgenticRag.Configuration;

/// <summary>
/// Root options bound from <c>appsettings.json</c> + environment overrides.
/// All endpoints are configurable so the same binary runs against the Pi tailnet
/// in production and against a local dev box without code changes.
/// </summary>
public sealed class AgenticRagOptions
{
    /// <summary>Ollama (LLM chat) endpoint config. Embedding lives in <see cref="EmbedPipelineOptions"/>.</summary>
    public OllamaOptions Ollama { get; set; } = new();

    /// <summary>Qdrant vector DB endpoint config.</summary>
    public QdrantOptions Qdrant { get; set; } = new();

    /// <summary>Karakeep bookmarks endpoint config (used in v1; reserved here so the shape is stable).</summary>
    public KarakeepOptions Karakeep { get; set; } = new();
}

/// <summary>Ollama endpoint and chat model selection. Embedding does NOT go through Ollama in v0.5.</summary>
public sealed class OllamaOptions
{
    /// <summary>Base URL of the Ollama server, e.g. <c>http://pi:11434</c>.</summary>
    public string Endpoint { get; set; } = "http://localhost:11434";

    /// <summary>Chat / tool-use model. Confirmed working: <c>qwen2.5:3b</c>.</summary>
    public string ChatModel { get; set; } = "qwen2.5:3b";
}

/// <summary>Qdrant endpoint, auth, collection, and named-vector selection.</summary>
public sealed class QdrantOptions
{
    /// <summary>
    /// gRPC endpoint URI for Qdrant, e.g. <c>http://pi:6334</c>.
    /// The SDK speaks gRPC only — point this at the gRPC port (6334), not the REST port (6333).
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:6334";

    /// <summary>API key for authentication. Empty = no auth.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Collection name holding the indexed vault chunks.</summary>
    public string Collection { get; set; } = "vault";

    /// <summary>
    /// Named vector to query against. The indexer writes embeddings under this name —
    /// passing it as <c>vectorName</c> on <c>SearchAsync</c> is mandatory for a multi-vector collection.
    /// </summary>
    public string VectorName { get; set; } = "fast-all-minilm-l6-v2";
}

/// <summary>Karakeep endpoint config (v1 — reserved).</summary>
public sealed class KarakeepOptions
{
    /// <summary>Karakeep API base URL.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Karakeep API token.</summary>
    public string ApiKey { get; set; } = string.Empty;
}
