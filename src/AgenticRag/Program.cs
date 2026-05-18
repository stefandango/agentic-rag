using System.Text.Json;
using System.Text.Json.Serialization;
using AgenticRag.Configuration;
using AgenticRag.Embedding;
using AgenticRag.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;

// Throwaway Phase B smoke test — embeds the query via the external pipeline,
// runs Qdrant search, prints JSON. Replaced when the agent loop lands.

var query = args.Length > 0
    ? string.Join(' ', args)
    : Console.In.ReadToEnd().Trim();

if (string.IsNullOrWhiteSpace(query))
{
    Console.Error.WriteLine("usage: dotnet run -- \"<query>\"   (or pipe the query on stdin)");
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

await using var provider = services.BuildServiceProvider();

var tools = provider.GetRequiredService<IKnowledgeTools>();
var result = await tools.SearchKnowledge(query, topK: 5);

var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
});
Console.WriteLine(json);
return result.Ok ? 0 : 1;
