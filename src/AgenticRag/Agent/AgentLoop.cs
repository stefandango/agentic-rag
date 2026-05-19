using AgenticRag.Llm;
using AgenticRag.Tools;

namespace AgenticRag.Agent;

/// <summary>
/// The v0.5 agent loop: one question in, a synthesised answer out. Hand-rolled —
/// chat → (tool calls → execute → feed back)* → text. No streaming, no history
/// across invocations, no retries.
/// </summary>
public sealed class AgentLoop(IChatClient chat, IKnowledgeTools tools)
{
    /// <summary>Max tool-call turns before a synthesis answer is forced.</summary>
    public const int MaxToolTurns = 5;

    /// <summary>
    /// Validated system prompt. The small-talk clause is the load-bearing mitigation
    /// against eager tool calls; the citation and ranking clauses are folded in.
    /// </summary>
    public const string SystemPrompt =
        "You are a retrieval assistant for the user's personal Obsidian vault. You have tools " +
        "that search and read notes in that vault. Only call a tool when answering the user " +
        "actually requires retrieving information from the vault. For greetings, small talk, or " +
        "anything you can answer directly from general knowledge, just reply normally and do not " +
        "call any tool. If no available tool fits the request, say so plainly instead of forcing " +
        "an unrelated tool call. " +
        "When your answer draws on retrieved notes, list the source notes at the end under a " +
        "'Sources:' heading, one per line as '- [Title] (path)', in the order you used them. " +
        "Filter/list tool results are not relevance-ranked; do not infer importance from their " +
        "order or score.";

    private const string BudgetNudge =
        "You've used your tool budget. Synthesize an answer from what you've found so far.";

    /// <summary>
    /// Run the loop for one question and return the assistant's final text.
    /// HTTP/transport failures propagate — the CLI surfaces them.
    /// </summary>
    public async Task<string> RunAsync(string question, CancellationToken ct = default)
    {
        var messages = new List<AgentMessage>
        {
            new(AgentRole.System, SystemPrompt),
            new(AgentRole.User, question),
        };

        var toolTurns = 0;
        while (true)
        {
            var atCap = toolTurns >= MaxToolTurns;
            if (atCap)
            {
                messages.Add(new AgentMessage(AgentRole.System, BudgetNudge));
            }

            var turn = await chat.CompleteAsync(
                messages,
                ToolCatalog.Specs,
                atCap ? ToolMode.None : ToolMode.Auto,
                ct).ConfigureAwait(false);

            messages.Add(new AgentMessage(
                AgentRole.Assistant,
                turn.Content,
                turn.ToolCalls.Count > 0 ? turn.ToolCalls : null));

            if (turn.ToolCalls.Count == 0 || atCap)
            {
                return turn.Content ?? "(no answer)";
            }

            foreach (var call in turn.ToolCalls)
            {
                var result = await ToolCatalog
                    .DispatchAsync(tools, call.Name, call.ArgumentsJson, ct)
                    .ConfigureAwait(false);

                messages.Add(new AgentMessage(
                    AgentRole.Tool,
                    result,
                    ToolCallId: call.Id,
                    ToolName: call.Name));
            }

            toolTurns++;
        }
    }
}
