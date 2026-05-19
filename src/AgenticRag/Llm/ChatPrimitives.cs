using System.Text.Json;

namespace AgenticRag.Llm;

/// <summary>Conversation roles, provider-neutral.</summary>
public enum AgentRole
{
    /// <summary>System / instruction message.</summary>
    System,
    /// <summary>End-user message.</summary>
    User,
    /// <summary>Model message (may carry tool calls).</summary>
    Assistant,
    /// <summary>Result of a tool execution, fed back to the model.</summary>
    Tool,
}

/// <summary>How the next completion may use tools.</summary>
public enum ToolMode
{
    /// <summary>Model decides whether to call tools.</summary>
    Auto,
    /// <summary>Tools forbidden — force a text synthesis turn.</summary>
    None,
}

/// <summary>A single tool call requested by the model.</summary>
/// <param name="Id">Provider call id, echoed back on the matching tool result. Synthesised if the provider omits one.</param>
/// <param name="Name">Tool/function name.</param>
/// <param name="ArgumentsJson">Raw JSON object of arguments (always a JSON string, even for providers that hand back a map).</param>
public sealed record AgentToolCall(string Id, string Name, string ArgumentsJson);

/// <summary>One conversation message in the provider-neutral shape the loop manipulates.</summary>
/// <param name="Role">Message role.</param>
/// <param name="Content">Text content. Null when an assistant message is purely tool calls.</param>
/// <param name="ToolCalls">Populated on assistant messages that requested tools.</param>
/// <param name="ToolCallId">On a <see cref="AgentRole.Tool"/> message: the call id it answers.</param>
/// <param name="ToolName">On a <see cref="AgentRole.Tool"/> message: the tool name (Ollama keys results by name, not id).</param>
public sealed record AgentMessage(
    AgentRole Role,
    string? Content,
    IReadOnlyList<AgentToolCall>? ToolCalls = null,
    string? ToolCallId = null,
    string? ToolName = null);

/// <summary>A tool the model may call: name, description, and a raw JSON-Schema parameters object.</summary>
/// <param name="Name">Function name the model invokes.</param>
/// <param name="Description">What the tool does and when to use it.</param>
/// <param name="Parameters">JSON-Schema <c>object</c> describing the arguments.</param>
public sealed record ToolSpec(string Name, string Description, JsonElement Parameters);

/// <summary>The model's response for one completion: free text and/or tool calls.</summary>
/// <param name="Content">Assistant text, if any.</param>
/// <param name="ToolCalls">Requested tool calls (empty when the model answered directly).</param>
public sealed record ChatTurn(string? Content, IReadOnlyList<AgentToolCall> ToolCalls);

/// <summary>
/// Provider-neutral chat client. One implementation per LLM profile
/// (<c>MistralChatClient</c>, <c>OllamaChatClient</c>); the agent loop only sees this.
/// </summary>
public interface IChatClient
{
    /// <summary>
    /// Run one completion. Implementations translate <paramref name="toolMode"/> to
    /// their provider's idiom (Mistral: <c>tool_choice</c>; Ollama: include/omit tools).
    /// </summary>
    Task<ChatTurn> CompleteAsync(
        IReadOnlyList<AgentMessage> messages,
        IReadOnlyList<ToolSpec> tools,
        ToolMode toolMode,
        CancellationToken ct);
}
