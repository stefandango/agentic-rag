using System.Text.Json;
using AgenticRag.Configuration;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;

namespace AgenticRag.Llm;

/// <summary>
/// Chat client for the Pi-Ollama fallback profile, via OllamaSharp's native chat API.
/// </summary>
/// <remarks>
/// Ollama has no <c>tool_choice</c>: <see cref="ToolMode.None"/> is expressed by simply
/// not sending tools. OllamaSharp's strongly-typed tool schema only models type/description/enum
/// (no nested array <c>items</c>), so array parameters are declared loosely — acceptable for the
/// fallback path; the Mistral default carries the full JSON-Schema.
/// </remarks>
public sealed class OllamaChatClient : IChatClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly OllamaApiClient _client;
    private readonly LlmProfile _p;

    public OllamaChatClient(IOptions<AgenticRagOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _p = options.Value.Llm.Ollama;
        _client = new OllamaApiClient(new Uri(_p.BaseUrl), _p.Model);
    }

    public async Task<ChatTurn> CompleteAsync(
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<ToolSpec> tools,
        ToolMode toolMode,
        CancellationToken ct)
    {
        var request = new ChatRequest
        {
            Model = _p.Model,
            Stream = false,
            Messages = messages.Select(ToOllama).ToList(),
            Tools = toolMode == ToolMode.None || tools.Count == 0
                ? null
                : tools.Select(ToOllamaTool).ToList(),
            Options = new RequestOptions
            {
                Temperature = (float)_p.Temperature,
                NumPredict = _p.MaxTokens > 0 ? _p.MaxTokens : null,
            },
        };

        Message? final = null;
        await foreach (var chunk in _client.ChatAsync(request, ct).ConfigureAwait(false))
        {
            if (chunk?.Message is { } m)
            {
                final = m;
            }
        }

        var content = final?.Content;
        var calls = (final?.ToolCalls ?? [])
            .Select((tc, i) => new AgentToolCall(
                string.IsNullOrEmpty(tc.Function?.Name) ? $"call_{i}" : (tc.Id ?? $"call_{i}"),
                tc.Function?.Name ?? string.Empty,
                tc.Function?.Arguments is { } a
                    ? JsonSerializer.Serialize(a, Json)
                    : "{}"))
            .Where(c => c.Name.Length > 0)
            .ToList();

        return new ChatTurn(content, calls);
    }

    private static Message ToOllama(AgentMessage m)
    {
        var role = m.Role switch
        {
            AgentRole.System => ChatRole.System,
            AgentRole.User => ChatRole.User,
            AgentRole.Assistant => ChatRole.Assistant,
            AgentRole.Tool => ChatRole.Tool,
            _ => ChatRole.User,
        };

        var msg = new Message
        {
            Role = role,
            Content = m.Content ?? string.Empty,
        };

        if (m.Role == AgentRole.Tool)
        {
            msg.ToolName = m.ToolName;
        }

        if (m.ToolCalls is { Count: > 0 })
        {
            msg.ToolCalls = m.ToolCalls.Select(tc => new Message.ToolCall
            {
                Id = tc.Id,
                Function = new Message.Function
                {
                    Name = tc.Name,
                    Arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                        string.IsNullOrWhiteSpace(tc.ArgumentsJson) ? "{}" : tc.ArgumentsJson, Json),
                },
            }).ToList();
        }

        return msg;
    }

    private static Tool ToOllamaTool(ToolSpec spec)
    {
        var props = new Dictionary<string, Property>();
        var required = new List<string>();

        if (spec.Parameters.ValueKind == JsonValueKind.Object
            && spec.Parameters.TryGetProperty("properties", out var p)
            && p.ValueKind == JsonValueKind.Object)
        {
            foreach (var field in p.EnumerateObject())
            {
                var type = field.Value.TryGetProperty("type", out var t) ? t.GetString() : "string";
                var desc = field.Value.TryGetProperty("description", out var d) ? d.GetString() : null;
                string[]? enumVals = field.Value.TryGetProperty("enum", out var e)
                        && e.ValueKind == JsonValueKind.Array
                    ? e.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray()
                    : null;

                props[field.Name] = new Property
                {
                    Type = type ?? "string",
                    Description = desc,
                    Enum = enumVals,
                };
            }
        }

        if (spec.Parameters.ValueKind == JsonValueKind.Object
            && spec.Parameters.TryGetProperty("required", out var r)
            && r.ValueKind == JsonValueKind.Array)
        {
            required.AddRange(r.EnumerateArray().Select(x => x.GetString() ?? string.Empty));
        }

        return new Tool
        {
            Type = "function",
            Function = new Function
            {
                Name = spec.Name,
                Description = spec.Description,
                Parameters = new Parameters
                {
                    Type = "object",
                    Properties = props,
                    Required = required,
                },
            },
        };
    }
}
