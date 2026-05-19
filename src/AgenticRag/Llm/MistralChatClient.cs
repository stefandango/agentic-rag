using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgenticRag.Configuration;
using Microsoft.Extensions.Options;

namespace AgenticRag.Llm;

/// <summary>
/// OpenAI-compatible chat client for the Mistral cloud profile.
/// One <c>POST {BaseUrl}/v1/chat/completions</c> per completion; no streaming, no retries.
/// </summary>
public sealed class MistralChatClient(HttpClient http, IOptions<AgenticRagOptions> options) : IChatClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly LlmProfile _p = options.Value.Llm.Mistral;

    public async Task<ChatTurn> CompleteAsync(
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<ToolSpec> tools,
        ToolMode toolMode,
        CancellationToken ct)
    {
        var request = new ReqBody(
            Model: _p.Model,
            Messages: messages.Select(ToWire).ToList(),
            Tools: tools.Count == 0 ? null : tools.Select(ToWireTool).ToList(),
            ToolChoice: tools.Count == 0 ? null : (toolMode == ToolMode.None ? "none" : "auto"),
            Temperature: _p.Temperature,
            MaxTokens: _p.MaxTokens > 0 ? _p.MaxTokens : null);

        var url = new Uri(new Uri(_p.BaseUrl), "/v1/chat/completions");
        using var content = new StringContent(
            JsonSerializer.Serialize(request, Json), Encoding.UTF8, "application/json");
        using var httpMsg = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

        var apiKey = string.IsNullOrEmpty(_p.ApiKeyEnv)
            ? null : Environment.GetEnvironmentVariable(_p.ApiKeyEnv);
        if (!string.IsNullOrEmpty(apiKey))
        {
            httpMsg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var response = await http.SendAsync(httpMsg, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Mistral returned {(int)response.StatusCode}: {Truncate(body, 500)}");
        }

        var parsed = await response.Content.ReadFromJsonAsync<RespBody>(Json, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Mistral returned an empty body");

        var choice = parsed.Choices is { Count: > 0 } ? parsed.Choices[0].Message : null;
        var calls = (choice?.ToolCalls ?? [])
            .Select((tc, i) => new AgentToolCall(
                string.IsNullOrEmpty(tc.Id) ? $"call_{i}" : tc.Id,
                tc.Function.Name,
                string.IsNullOrWhiteSpace(tc.Function.Arguments) ? "{}" : tc.Function.Arguments))
            .ToList();

        return new ChatTurn(choice?.Content, calls);
    }

    private static ReqMessage ToWire(AgentMessage m) => new(
        Role: m.Role switch
        {
            AgentRole.System => "system",
            AgentRole.User => "user",
            AgentRole.Assistant => "assistant",
            AgentRole.Tool => "tool",
            _ => "user",
        },
        Content: m.Content,
        ToolCallId: m.ToolCallId,
        ToolCalls: m.ToolCalls is null || m.ToolCalls.Count == 0
            ? null
            : m.ToolCalls.Select(tc => new ReqToolCall(
                tc.Id, "function", new ReqFnCall(tc.Name, tc.ArgumentsJson))).ToList());

    private static ReqTool ToWireTool(ToolSpec t) =>
        new("function", new ReqToolFn(t.Name, t.Description, t.Parameters));

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    // ---- request wire shapes ----
    private sealed record ReqBody(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ReqMessage> Messages,
        [property: JsonPropertyName("tools")] IReadOnlyList<ReqTool>? Tools,
        [property: JsonPropertyName("tool_choice")] string? ToolChoice,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_tokens")] int? MaxTokens);

    private sealed record ReqMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("tool_call_id")] string? ToolCallId,
        [property: JsonPropertyName("tool_calls")] IReadOnlyList<ReqToolCall>? ToolCalls);

    private sealed record ReqToolCall(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("function")] ReqFnCall Function);

    private sealed record ReqFnCall(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("arguments")] string Arguments);

    private sealed record ReqTool(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("function")] ReqToolFn Function);

    private sealed record ReqToolFn(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("parameters")] JsonElement Parameters);

    // ---- response wire shapes ----
    private sealed record RespBody(
        [property: JsonPropertyName("choices")] IReadOnlyList<RespChoice>? Choices);

    private sealed record RespChoice(
        [property: JsonPropertyName("message")] RespMessage Message);

    private sealed record RespMessage(
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("tool_calls")] IReadOnlyList<RespToolCall>? ToolCalls);

    private sealed record RespToolCall(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("function")] RespFnCall Function);

    private sealed record RespFnCall(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("arguments")] string Arguments);
}
