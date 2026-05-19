using AgenticRag.Agent;
using AgenticRag.Llm;
using AgenticRag.Tools;
using Xunit;

namespace AgenticRag.Tests;

/// <summary>
/// Exercises the loop's <em>handling</em> of model outputs (no-call, single call,
/// malformed args, multi-call, budget cap) with a scripted client — never asserts
/// real model behaviour.
/// </summary>
public sealed class AgentLoopTests
{
    [Fact]
    public async Task No_tool_call_returns_content_in_one_turn()
    {
        var chat = new FakeChatClient(new ChatTurn("hello there", []));
        var loop = new AgentLoop(chat, new StubKnowledgeTools());

        var answer = await loop.RunAsync("hi");

        Assert.Equal("hello there", answer);
        Assert.Equal(1, chat.Calls);
        Assert.Equal(ToolMode.Auto, chat.LastToolMode);
    }

    [Fact]
    public async Task Single_tool_call_is_executed_then_synthesised()
    {
        var chat = new FakeChatClient(
            new ChatTurn(null, [new AgentToolCall("c1", "search_knowledge", """{"query":"x"}""")]),
            new ChatTurn("answer with Sources:", []));
        var tools = new StubKnowledgeTools();
        var loop = new AgentLoop(chat, tools);

        var answer = await loop.RunAsync("what did I note about x");

        Assert.Equal("answer with Sources:", answer);
        Assert.Equal(2, chat.Calls);
        Assert.Equal(1, tools.SearchCalls);
        // The second completion saw a tool-result message.
        Assert.Contains(chat.LastMessages, m => m.Role == AgentRole.Tool && m.ToolName == "search_knowledge");
    }

    [Fact]
    public async Task Malformed_arguments_become_an_error_message_and_the_loop_continues()
    {
        var chat = new FakeChatClient(
            new ChatTurn(null, [new AgentToolCall("c1", "search_knowledge", "not json")]),
            new ChatTurn("recovered", []));
        var loop = new AgentLoop(chat, new StubKnowledgeTools());

        var answer = await loop.RunAsync("q");

        Assert.Equal("recovered", answer);
        var toolMsg = Assert.Single(chat.LastMessages, m => m.Role == AgentRole.Tool);
        Assert.Contains("could not parse tool arguments", toolMsg.Content);
    }

    [Fact]
    public async Task Multiple_tool_calls_in_one_turn_all_dispatch_before_next_completion()
    {
        var chat = new FakeChatClient(
            new ChatTurn(null,
            [
                new AgentToolCall("a", "search_knowledge", """{"query":"x"}"""),
                new AgentToolCall("b", "list_recent_daily_notes", """{"days":7}"""),
            ]),
            new ChatTurn("done", []));
        var tools = new StubKnowledgeTools();
        var loop = new AgentLoop(chat, tools);

        await loop.RunAsync("q");

        Assert.Equal(1, tools.SearchCalls);
        Assert.Equal(1, tools.DailyCalls);
        var toolMsgs = chat.LastMessages.Where(m => m.Role == AgentRole.Tool).ToList();
        Assert.Equal(2, toolMsgs.Count);
        Assert.Equal(["a", "b"], toolMsgs.Select(m => m.ToolCallId));
    }

    [Fact]
    public async Task Tool_call_loop_is_capped_and_forces_a_no_tools_synthesis()
    {
        // Always asks for a tool — would loop forever without the cap.
        var chat = new FakeChatClient { Default = new ChatTurn(null,
            [new AgentToolCall("c", "search_knowledge", """{"query":"x"}""")]) };
        chat.OverrideAtCall(AgentLoop.MaxToolTurns + 1, new ChatTurn("forced synthesis", []));
        var loop = new AgentLoop(chat, new StubKnowledgeTools());

        var answer = await loop.RunAsync("q");

        Assert.Equal("forced synthesis", answer);
        Assert.Equal(AgentLoop.MaxToolTurns + 1, chat.Calls);
        Assert.Equal(ToolMode.None, chat.LastToolMode); // budget turn forbids tools
        Assert.Contains(chat.LastMessages,
            m => m.Role == AgentRole.System && m.Content!.Contains("tool budget"));
    }

    // ---- test doubles ----

    private sealed class FakeChatClient : IChatClient
    {
        private readonly Queue<ChatTurn> _scripted = new();
        private readonly Dictionary<int, ChatTurn> _overrides = [];

        public FakeChatClient(params ChatTurn[] scripted)
        {
            foreach (var t in scripted)
            {
                _scripted.Enqueue(t);
            }
        }

        public ChatTurn Default { get; init; } = new("(default)", []);
        public int Calls { get; private set; }
        public ToolMode LastToolMode { get; private set; }
        public IReadOnlyList<AgentMessage> LastMessages { get; private set; } = [];

        public void OverrideAtCall(int callNumber, ChatTurn turn) => _overrides[callNumber] = turn;

        public Task<ChatTurn> CompleteAsync(
            IReadOnlyList<AgentMessage> messages,
            IReadOnlyList<ToolSpec> tools,
            ToolMode toolMode,
            CancellationToken ct)
        {
            Calls++;
            LastToolMode = toolMode;
            LastMessages = messages.ToList();

            if (_overrides.TryGetValue(Calls, out var o))
            {
                return Task.FromResult(o);
            }
            return Task.FromResult(_scripted.Count > 0 ? _scripted.Dequeue() : Default);
        }
    }

    private sealed class StubKnowledgeTools : IKnowledgeTools
    {
        public int SearchCalls { get; private set; }
        public int DailyCalls { get; private set; }

        public Task<ToolResult<IReadOnlyList<SearchHit>>> SearchKnowledge(
            string query, int topK = 5, string[]? sources = null, string[]? tags = null,
            string? type = null, string[]? folders = null, CancellationToken ct = default)
        {
            SearchCalls++;
            var hit = new SearchHit(
                "id1", SearchHitSource.Vault, "notes/x.md", "X", "snippet", 0.9f,
                new HitMetadata(null, null, [], new VaultMetadata(null, "note", [], null, "X"), null));
            return Task.FromResult(
                ToolResult<IReadOnlyList<SearchHit>>.Success([hit]));
        }

        public Task<ToolResult<NoteContent>> GetNoteByPath(string path, CancellationToken ct = default)
            => Task.FromResult(ToolResult<NoteContent>.Success(
                new NoteContent(path, "body", new Dictionary<string, object?> { ["type"] = "note" })));

        public Task<ToolResult<IReadOnlyList<SearchHit>>> SearchByTagOrType(
            string[]? tags, string? type, int limit = 20, CancellationToken ct = default)
            => Task.FromResult(ToolResult<IReadOnlyList<SearchHit>>.Success([]));

        public Task<ToolResult<IReadOnlyList<DailyNoteRef>>> ListRecentDailyNotes(
            int days, CancellationToken ct = default)
        {
            DailyCalls++;
            return Task.FromResult(
                ToolResult<IReadOnlyList<DailyNoteRef>>.Success(
                    [new DailyNoteRef(new DateOnly(2026, 5, 1), "daily/2026-05-01.md", null)]));
        }
    }
}
