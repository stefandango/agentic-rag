using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AgenticRag.Embedding;

/// <summary>
/// Typed HTTP client for the external embed pipeline. Single endpoint:
/// <c>POST {Endpoint}{EmbedPath}</c> with body <c>{"text": "..."}</c> returns
/// <c>{"vector": [...float32 array...], "model": "...", "dim": 384}</c>.
/// </summary>
/// <remarks>
/// Exceptions are intentionally NOT caught here — the caller (<c>VaultTools</c>)
/// translates HTTP / serialization failures into <c>ToolResult.Failure</c>.
/// </remarks>
public sealed class EmbedPipelineClient(HttpClient http, IOptions<EmbedPipelineOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly EmbedPipelineOptions _opts = options.Value;

    /// <summary>Embed a single text string. Returns the 384-dim float32 vector.</summary>
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var url = new Uri(new Uri(_opts.Endpoint), _opts.EmbedPath);

        // Intentional: do NOT use PostAsJsonAsync here. The pipeline's WSGI/ASGI layer
        // 400s on the chunked-Transfer-Encoding body that PostAsJsonAsync emits with no
        // pre-known length; StringContent computes Content-Length up-front and goes through.
        var body = JsonSerializer.Serialize(new EmbedRequest(text), JsonOptions);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(url, content, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException(
                $"embed pipeline returned {(int)response.StatusCode}: {errBody}");
        }

        var payload = await response.Content.ReadFromJsonAsync<EmbedResponse>(JsonOptions, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("embed pipeline returned an empty body");

        return payload.Vector;
    }

    private sealed record EmbedRequest([property: JsonPropertyName("text")] string Text);

    private sealed record EmbedResponse(
        [property: JsonPropertyName("vector")] float[] Vector,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("dim")] int Dim);
}
