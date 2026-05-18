using AgenticRag.Configuration;
using AgenticRag.Embedding;
using AgenticRag.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Xunit;

namespace AgenticRag.Tests;

/// <summary>
/// Shared, one-per-test-class wiring. Builds the same DI graph as <c>Program.cs</c>
/// (config from <c>appsettings.json</c> + <c>AGENTICRAG_</c> env + <c>QDRANT_API_KEY</c>),
/// resolves the real <see cref="IKnowledgeTools"/>, and probes Qdrant once so
/// integration tests can skip cleanly when the tailnet is unreachable.
/// </summary>
public sealed class KnowledgeToolsFixture : IAsyncLifetime
{
    private ServiceProvider _provider = null!;

    /// <summary>The real tool implementation wired against the configured Qdrant.</summary>
    public IKnowledgeTools Tools { get; private set; } = null!;

    /// <summary>True when the configured Qdrant has the configured collection.</summary>
    public bool QdrantReachable { get; private set; }

    /// <summary>Reason shown by skipped integration tests when Qdrant is unavailable.</summary>
    public string SkipReason =>
        "Configured Qdrant collection not reachable (no appsettings.Development.json, " +
        "or endpoint down) — integration test skipped.";

    public async Task InitializeAsync()
    {
        // Mirror Program.cs: localhost defaults, then the Pi override if present.
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables(prefix: "AGENTICRAG_")
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.Configure<AgenticRagOptions>(config);
        services.Configure<EmbedPipelineOptions>(config.GetSection(EmbedPipelineOptions.SectionName));
        services.AddHttpClient<EmbedPipelineClient>();

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AgenticRagOptions>>().Value.Qdrant;
            var envKey = Environment.GetEnvironmentVariable("QDRANT_API_KEY");
            var apiKey = !string.IsNullOrEmpty(envKey) ? envKey
                : string.IsNullOrEmpty(opts.ApiKey) ? null : opts.ApiKey;
            return new QdrantClient(new Uri(opts.Endpoint), apiKey: apiKey);
        });
        services.AddSingleton<IKnowledgeTools, KnowledgeTools>();

        _provider = services.BuildServiceProvider();
        Tools = _provider.GetRequiredService<IKnowledgeTools>();

        // One bounded probe of the *collection* (not just server health): tests
        // gate on this so a wrong/empty Qdrant skips instead of failing, and no
        // test pays the connect timeout against a dead endpoint.
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var qdrant = _provider.GetRequiredService<QdrantClient>();
            var collection = _provider.GetRequiredService<IOptions<AgenticRagOptions>>()
                .Value.Qdrant.Collection;
            QdrantReachable = await qdrant.CollectionExistsAsync(collection, cts.Token);
        }
        catch
        {
            QdrantReachable = false;
        }
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    /// <summary>Minimal no-op logger provider so DI has something to resolve.</summary>
    private sealed class NullLoggerProvider : ILoggerProvider
    {
        public static readonly NullLoggerProvider Instance = new();
        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;
        public void Dispose() { }

        private sealed class NullLogger : ILogger
        {
            public static readonly NullLogger Instance = new();
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => Scope.Instance;
            public bool IsEnabled(LogLevel logLevel) => false;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter) { }

            private sealed class Scope : IDisposable
            {
                public static readonly Scope Instance = new();
                public void Dispose() { }
            }
        }
    }
}

/// <summary>Binds the fixture to a collection so it is built once for all tool tests.</summary>
[CollectionDefinition(Name)]
public sealed class KnowledgeToolsCollection : ICollectionFixture<KnowledgeToolsFixture>
{
    public const string Name = "knowledge-tools";
}
