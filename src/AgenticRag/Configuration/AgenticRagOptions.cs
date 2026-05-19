namespace AgenticRag.Configuration;

/// <summary>
/// Root options bound from <c>appsettings.json</c> + environment overrides.
/// All endpoints are configurable so the same binary runs against the Pi tailnet
/// in production and against a local dev box without code changes.
/// </summary>
public sealed class AgenticRagOptions
{
    /// <summary>LLM chat/tool-use config (profile selection + per-profile settings).</summary>
    public LlmOptions Llm { get; set; } = new();

    /// <summary>Qdrant vector DB endpoint config.</summary>
    public QdrantOptions Qdrant { get; set; } = new();

    /// <summary>Karakeep bookmarks endpoint config (used in v1; reserved here so the shape is stable).</summary>
    public KarakeepOptions Karakeep { get; set; } = new();
}

/// <summary>
/// LLM configuration. The agent loop is provider-agnostic: it talks to whichever
/// profile <see cref="Profile"/> selects via the <c>IChatClient</c> abstraction.
/// </summary>
public sealed class LlmOptions
{
    /// <summary>Active profile name — <c>mistral</c> (default) or <c>ollama</c> (fallback).</summary>
    public string Profile { get; set; } = "mistral";

    /// <summary>Mistral cloud profile (OpenAI-compatible HTTP).</summary>
    public LlmProfile Mistral { get; set; } = new()
    {
        BaseUrl = "https://api.mistral.ai",
        Model = "mistral-medium-latest",
        ApiKeyEnv = "MISTRAL_API_KEY",
    };

    /// <summary>Pi-Ollama fallback profile (via OllamaSharp).</summary>
    public LlmProfile Ollama { get; set; } = new()
    {
        BaseUrl = "http://localhost:11434",
        Model = "qwen2.5:3b",
        ApiKeyEnv = null,
    };

    /// <summary>The profile <see cref="Profile"/> resolves to. Defaults to Mistral on an unknown value.</summary>
    public LlmProfile Active =>
        string.Equals(Profile, "ollama", StringComparison.OrdinalIgnoreCase) ? Ollama : Mistral;

    /// <summary>True when the active profile is the Ollama fallback.</summary>
    public bool ActiveIsOllama =>
        string.Equals(Profile, "ollama", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One LLM endpoint profile. Same shape for cloud and local.</summary>
public sealed class LlmProfile
{
    /// <summary>Base URL, e.g. <c>https://api.mistral.ai</c> or <c>http://your-host:11434</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Chat/tool-use model name.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Env var holding the bearer API key. Null/empty = no auth (local Ollama).</summary>
    public string? ApiKeyEnv { get; set; }

    /// <summary>Max completion tokens. 0 = leave to the provider default.</summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>Sampling temperature. 0 for deterministic tool-use turns.</summary>
    public double Temperature { get; set; }
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
