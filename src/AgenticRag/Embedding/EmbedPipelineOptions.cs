namespace AgenticRag.Embedding;

/// <summary>
/// Endpoint configuration for the external embed pipeline (the same Python service
/// the indexer uses to write vectors). Calling it from .NET on the query side guarantees
/// the query vector is produced by the identical model bytes — no drift.
/// </summary>
public sealed class EmbedPipelineOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "EmbedPipeline";

    /// <summary>Base URL, e.g. <c>http://pi:8000</c>. Tailnet-only in prod.</summary>
    public string Endpoint { get; set; } = "http://localhost:8000";

    /// <summary>Path of the embed endpoint, relative to <see cref="Endpoint"/>.</summary>
    public string EmbedPath { get; set; } = "/embed";
}
