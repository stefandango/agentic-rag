using AgenticRag.Agent;
using AgenticRag.Configuration;
using AgenticRag.Embedding;
using AgenticRag.Llm;
using AgenticRag.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;

// v0.5 agent loop CLI: one question in, a synthesised answer out. One shot, no REPL.

var query = args.Length > 0
    ? string.Join(' ', args)
    : Console.In.ReadToEnd().Trim();

if (string.IsNullOrWhiteSpace(query))
{
    Console.Error.WriteLine("usage: dotnet run -- \"<question>\"   (or pipe the question on stdin)");
    return 2;
}

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables(prefix: "AGENTICRAG_")
    .Build();

var services = new ServiceCollection();

services.AddLogging(b => b
    .AddConfiguration(config.GetSection("Logging"))
    .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }));

services.Configure<AgenticRagOptions>(config);
services.Configure<EmbedPipelineOptions>(config.GetSection(EmbedPipelineOptions.SectionName));

services.AddHttpClient<EmbedPipelineClient>();

services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<AgenticRagOptions>>().Value.Qdrant;
    // QDRANT_API_KEY env var wins over the config value so the key can rotate
    // without editing appsettings; fall back to Qdrant.ApiKey, then no auth.
    var envKey = Environment.GetEnvironmentVariable("QDRANT_API_KEY");
    var apiKey = !string.IsNullOrEmpty(envKey) ? envKey
        : string.IsNullOrEmpty(opts.ApiKey) ? null : opts.ApiKey;
    return new QdrantClient(new Uri(opts.Endpoint), apiKey: apiKey);
});

services.AddSingleton<IKnowledgeTools, KnowledgeTools>();

// Both LLM profiles live behind IChatClient; the loop never learns which is active.
services.AddHttpClient<MistralChatClient>();
services.AddSingleton<IChatClient>(sp =>
{
    var llm = sp.GetRequiredService<IOptions<AgenticRagOptions>>().Value.Llm;
    return llm.ActiveIsOllama
        ? new OllamaChatClient(sp.GetRequiredService<IOptions<AgenticRagOptions>>())
        : sp.GetRequiredService<MistralChatClient>();
});
services.AddSingleton<AgentLoop>();

await using var provider = services.BuildServiceProvider();

// TEMPORARY retrieval diagnostic — remove after confabulation triage.
// usage: dotnet run -- --search-debug <topK> <query...>
if (args.Length >= 3 && args[0] == "--search-debug")
{
    var dbgTopK = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
    var dbgQuery = string.Join(' ', args.Skip(2));
    var kt = provider.GetRequiredService<IKnowledgeTools>();
    var r = await kt.SearchKnowledge(dbgQuery, topK: dbgTopK);
    if (!r.Ok)
    {
        Console.Error.WriteLine("SearchKnowledge failed: " + r.Error);
        return 1;
    }
    var rank = 1;
    foreach (var h in r.Value!)
    {
        var snip = h.Snippet.ReplaceLineEndings(" ");
        if (snip.Length > 220)
        {
            snip = snip[..220] + "…";
        }
        Console.WriteLine($"#{rank,-2} score={h.Score:F4} type={h.Metadata.Vault?.NoteType}");
        Console.WriteLine($"    path : {h.Path}");
        Console.WriteLine($"    title: {h.Title}");
        Console.WriteLine($"    snip : {snip}");
        rank++;
    }
    return 0;
}

var profile = provider.GetRequiredService<IOptions<AgenticRagOptions>>().Value.Llm.Profile;
var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("agentic-rag");

try
{
    var loop = provider.GetRequiredService<AgentLoop>();
    var answer = await loop.RunAsync(query);
    Console.WriteLine(answer);
    return 0;
}
catch (Exception ex)
{
    logger.LogError(ex, "agent loop failed (profile {Profile})", profile);
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}
