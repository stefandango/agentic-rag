# agentic-rag

Agentic retrieval over an Obsidian vault (indexed in Qdrant) and Karakeep bookmarks. .NET console app, hand-rolled tool dispatch (no Semantic Kernel), Ollama-backed LLM.

> **v0.5 in progress** — read-only tool surface only. Phase A (interface) and Phase B (`SearchVault` end-to-end) landed. Agent loop, MCP server, and write tools are v1 work.

## Architecture (planned)

```
[ user query ]
      │
      ▼
┌──────────────┐    tools     ┌───────────────────────────┐
│  agent loop  │ ───────────► │  IVaultTools (read-only)  │
│  (qwen2.5)   │ ◄─────────── │  • SearchVault            │
└──────────────┘   results    │  • GetNoteByPath          │
      ▲                       │  • SearchByTagOrType      │
      │                       │  • ListRecentDailyNotes   │
      │                       │  • SearchKarakeep         │
      │                       └───────────┬───────────────┘
      │                                   │
      │                          ┌────────┴────────┐
      │                          ▼                 ▼
   Ollama                    Qdrant            Karakeep
   (Pi 5)                    (Pi 5)            (v1)
```

The vault is indexed by an external Python pipeline (`embed_vault.py`, FastEmbed-ONNX, `sentence-transformers/all-MiniLM-L6-v2`). This service only **queries** that index — it does not own indexing.

## Stack

- .NET 10, console app, central package management
- `OllamaSharp` (chat + embeddings)
- `Qdrant.Client` (gRPC)
- `Microsoft.Extensions.Configuration.{Json,EnvironmentVariables,Binder}` + `Logging`

## Embedding

Query vectors are produced by calling the same external embed pipeline that the indexer uses (`POST /embed`, `{"text": "..."}` → `{"vector": [...384 floats]}`). Same model bytes on both sides — no drift between index-time and query-time embeddings.

## Configuration

Endpoints come from `appsettings.json`, optionally overridden by `appsettings.Development.json` (gitignored) or environment variables prefixed `AGENTICRAG_` (`__` is the section separator):

```bash
export AGENTICRAG_QDRANT__ENDPOINT=http://pi.tailnet:6334
export AGENTICRAG_QDRANT__APIKEY=...
export AGENTICRAG_EMBEDPIPELINE__ENDPOINT=http://pi.tailnet:8000
export AGENTICRAG_OLLAMA__ENDPOINT=http://pi.tailnet:11434
```

Or copy `src/AgenticRag/appsettings.Development.json.example` to `appsettings.Development.json` and fill it in.

> **Note:** `Qdrant.Endpoint` is the **gRPC** port (default 6334), not REST (6333). The .NET SDK speaks gRPC.

## Smoke test

Tailnet-reachable Pi must have:
- Qdrant running on gRPC port 6334 with the `vault` collection (named vector `fast-all-minilm-l6-v2`)
- Embed pipeline service exposed on `/embed`

Then:

```bash
dotnet run --project src/AgenticRag -- "what is the morning brief automation"
```

Prints a `ToolResult<IReadOnlyList<SearchHit>>` envelope as snake_case JSON.

## Commands

- `dotnet build` — build
- `dotnet test` — no-op until a test project is added
- `dotnet format` — format
- `dotnet run --project src/AgenticRag -- "<query>"` — smoke test
