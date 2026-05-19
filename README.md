# agentic-rag

An agentic retrieval system over a personal knowledge base. A .NET console agent
takes a question, decides *how* to search — which tool, which filters, whether to
look again — runs the retrieval against an indexed knowledge store, and synthesises
an answer with citations back to the source notes.

"Agentic" is the distinction from plain RAG. Plain RAG embeds the query, pulls
top-K chunks, and stuffs them into one prompt. Here the LLM drives the retrieval:
it picks between semantic search, a metadata filter, a direct note fetch, or a
date-ranged listing, and it can chain calls when the first result isn't enough.

The tool surface is source-agnostic by design. v0.5 ships with the Obsidian vault
as the only source; v1 adds Karakeep bookmarks via the data-source integration
template, and the tool surface doesn't change.

## Architecture (v0.5)

```
                          your machine (any Headscale-joined host)
  ┌──────────────────────────────────────────────────────────────────┐
  │                                                                    │
  │   CLI            hand-rolled agent loop            IKnowledgeTools  │
  │  "question"  ──►  ┌───────────────────┐  dispatch  ┌────────────┐  │
  │                   │ 1. tool-use turn  │ ─────────► │ search /   │  │
  │                   │ 2. execute tool   │            │ fetch /    │  │
  │                   │ 3. synthesis turn │ ◄───────── │ list       │  │
  │                   └─────────┬─────────┘  results   └─────┬──────┘  │
  │     answer  ◄───────────────┘                            │         │
  │  + Sources:                                              │         │
  └───────────────────────────┬──────────────────────────────┼────────┘
                               │ chat + tool-use              │ over the tailnet
                               ▼                              ▼
                    ┌─────────────────────┐      ┌──────────────────────────┐
                    │  Mistral API (EU)   │      │  Raspberry Pi 5           │
                    │  mistral-medium     │      │  embed-pipeline /embed    │
                    │  (default profile)  │      │  Qdrant (gRPC, vault)     │
                    └─────────────────────┘      └──────────────────────────┘
```

The agent owns orchestration and synthesis. It owns no data: query vectors come
from the same embed-pipeline that built the index, and retrieval hits come from a
Qdrant collection populated by that pipeline. Both run on a Raspberry Pi and are
reachable only over the Headscale tailnet. The LLM is Mistral's EU-jurisdictional
API by default; vault chunks travel to it as tool results.

## What v0.5 does

Four read tools, exposed to the model as JSON-schema functions:

- **`search_knowledge`** — semantic search over the index, with optional `tags`,
  `type`, and `folders` filters. Returns ranked hits with the chunk body.
- **`get_note_by_path`** — fetch a full note by vault-relative path.
  Reconstructed from indexed chunks, not read from disk (see *Data layer*).
- **`search_by_tag_or_type`** — filter-only listing, no semantic query. Results
  are not relevance-ranked; the model is told not to infer importance from order.
- **`list_recent_daily_notes`** — daily notes from the last N days, newest first.

The loop is one question in, one synthesised answer out — no REPL, no history
across invocations. It runs a tool-use turn, executes any requested calls, feeds
the results back, and repeats until the model answers in prose or a five-turn
budget forces synthesis. When the answer draws on retrieved notes it ends with a
`Sources:` block, one `- [Title] (path)` line per note, deduplicated by path.

A worked example, against the default Mistral profile:

```
$ dotnet run --project src/AgenticRag -- "what did I decide about embedding models"
# ... structured HTTP logs on stdout elided ...
You decided the following about embedding models in your **agentic-rag** project:

1. **Flagship Model**:
   - **Model**: `sentence-transformers/all-MiniLM-L6-v2`
   - **Rationale**: Reuse the existing pipeline and Qdrant collection, which is already indexed with this model at section-level granularity. This avoids unnecessary rebuilding and maintains consistency.
   - **Constraint**: The Qdrant collection uses a named vector (`fast-all-minilm-l6-v2`), so any query must specify this vector name to avoid errors.

2. **Spike Model**:
   - The spike (experimental/prototype) embedding model can differ (e.g., `nomic-embed-text` via Ollama) since it uses a throwaway corpus. This flexibility allows for testing without affecting the production pipeline.

3. **Query Embedding**:
   - The agent embeds queries using the same `all-MiniLM-L6-v2` model via an HTTP endpoint (`/embed`) exposed by the `embed-pipeline` service. This ensures bit-identical vectors between queries and the indexed corpus, resolving potential embedding-model-drift issues.

4. **Operational Constraints**:
   - The embedding endpoint is bound to the Pi's Headscale IP (`your-host:8000`), making it accessible only within the Tailnet.

---

### Sources:
- [Decisions](projects/agentic-rag/index.md)
```

The answer is grounded in retrieved chunks — the model is summarising the project's
own decision record, not its priors.

## What v0.5 does not do

No Karakeep ingestion in any form. No write-back — every tool is read-only. No MCP
server mode. No multi-step query reformulation beyond what one loop's worth of tool
calls covers. No scheduled jobs, no inbox watcher. No observability or tracing. No
web UI — the interface is the CLI. These are v0.5's boundaries, listed so the scope
is unambiguous.

## The two LLM profiles

The agent loop talks to an `IChatClient` and never learns which profile is active.
Two are wired:

- **Mistral API (default)** — `mistral-medium-latest` via Mistral's
  EU-jurisdictional endpoint. Vault chunks travel to Mistral as tool results. This
  is a "your infrastructure + an EU-jurisdiction LLM" data story, not a
  fully-local one — accurate framing matters more than a cleaner claim.
- **Pi-Ollama (fallback)** — `qwen2.5:3b` on a Raspberry Pi 5. Fully local;
  nothing leaves the tailnet. It is the demonstrably offline-capable path, not the
  daily driver — query latency is around 45 seconds.

Real measurements, not extrapolation:

| Path | Single-turn tool call | Two-turn end-to-end query |
|---|---|---|
| Mistral `mistral-medium-latest` | 0.44–2.56s (typically ~0.5–0.8s) | ~8s |
| qwen2.5:3b on Pi 5 (8GB) | 10–32s | ~45s |

*Pi figures from the tool-use suite run 2026-04-24; Mistral figures from the same
suite re-run against `mistral-medium-latest` 2026-05-17. The Mistral cold-start
outlier (2.56s) settles to sub-second on subsequent calls.*

Switching profiles is a one-line config change (`Llm:Profile` = `mistral` or
`ollama`), no code change.

## Requirements to run it

- **.NET 10 SDK.**
- **A Mistral API key**, in the `MISTRAL_API_KEY` environment variable (for the
  default profile).
- **A Headscale-joined host.** The embed-pipeline `/embed` endpoint and Qdrant are
  bound to the Pi's tailnet IP only. The agent must run on a machine joined to the
  mesh — this is a hard constraint, not a convenience.
- **The embed-pipeline running and reachable.** It produces query vectors with the
  same model that built the index. It is a separate component, not part of this
  repo.
- **A Qdrant collection with content already indexed**, in the payload shape below.
  Also produced by the separate embed-pipeline.
- **Pi-Ollama profile only:** `ollama` on a tailnet host with `qwen2.5:3b` pulled.

Endpoints come from `appsettings.json`, overridable by a gitignored
`appsettings.Development.json` or `AGENTICRAG_`-prefixed environment variables.
`MISTRAL_API_KEY` and `QDRANT_API_KEY` are read from the environment so keys can
rotate without editing config.

## The data layer dependency

This agent queries an index it does not build. The embed-pipeline that produces
that index is a **separate repository** and a hard prerequisite. The contract
between them is the Qdrant payload: every point carries `file`, `title`,
`chunk_index`, `tags`, `type`, `folders`, `source`, a `heading`, and the chunk
body. The collection uses a named vector (`fast-all-minilm-l6-v2`) over gRPC.

The `file` payload key is a cross-system contract. The agent reads it to group and
fetch notes; the embed-pipeline writes and filters on it, including its
delete-by-file reindex dedup. Rename it on either side and the other breaks
silently — flag this before forking the work.

Because there is no filesystem access, `get_note_by_path` reconstructs a note from
its indexed chunks and rebuilds frontmatter from payload fields. Original YAML
formatting and non-indexed keys are not preserved. This is fine for feeding
context to an LLM; it is *not* a fidelity-preserving read, which is why v1's
annotation tools will read from disk instead.

## Architecture decisions worth flagging

**Source-agnostic tool surface, vault-only index.** The search tool is
`SearchKnowledge` with a `sources` filter, not `SearchVault`, even though the vault
is the only thing indexed today. Per-source tools (`search_vault`,
`search_karakeep`, …) are a fan-out anti-pattern: the agent ends up choosing which
source to query instead of the system unifying retrieval. v1 adds Karakeep as a
second `source` in the same collection — the tool surface stays put.

**Query vectors come from the embed-pipeline's HTTP endpoint.** Query and index
vectors must come from the same model or similarity scores are meaningless. Rather
than load `sentence-transformers` into the .NET process — a ~90MB model, an
ONNX conversion step, and a second copy of a service that already runs — the agent
calls the pipeline's `/embed` endpoint and gets bit-identical vectors. The drift
question disappears instead of being verified away.

**Hand-rolled agent loop, no Semantic Kernel.** Four tools, one provider with one
fallback, a single-turn CLI, no cross-conversation state. SK's tool-registration
and orchestration abstractions buy nothing at this scope, and the rest of the
codebase already talks to Qdrant and HTTP directly. SK would be the right call
for multi-step retrieval, multi-provider routing, or MCP server mode — that's a
v1 reason to revisit, not a v0.5 one.

**Mistral default, Pi-Ollama as a profile.** The project's thesis is self-reliant
infrastructure, which argues for the local model. But 45-second queries make a
tool a demo, not something used daily, and "actually used" was weighted above
thesis purity. Mistral closes the latency gap ~15–20× and is EU-jurisdictional,
preserving a defensible data-sovereignty story; keeping Pi-Ollama as a one-config
switch preserves the offline path without keeping dead code.

## Roadmap (v1)

Rough priority order, not commitments:

1. **Karakeep bookmarks**, via the data-source integration template — a webhook
   listener fetches each new bookmark, embeds it with the same model, and writes
   it into the same Qdrant collection with `source: "karakeep"`. The agent's tool
   surface does not change; the index gains a second source. This is also the
   reference implementation that makes later sources (Miniflux, Mealie, …) cheap
   additive work rather than rearchitectures.
2. **Annotation-only write-back** — append frontmatter, summaries, and proposed
   filing into existing notes; create files in `daily/` and `inbox/`. Never move,
   delete, or overwrite human-written content.
3. **Scheduled jobs** — a morning brief and similar, delivered as a vault file.
4. **Inbox watcher** — annotate new `inbox/` files with a proposed location and
   tags for review during normal triage.
5. **MCP server mode** — expose the same tools so Claude or any MCP host can use
   them without an adapter.

v1's order will be reshaped by living with v0.5: whatever hurts most in real use
gets priority over this list. That's a deliberate discipline, not vagueness.

## Related components

- **embed-pipeline** — the separate service that chunks the vault, embeds it with
  `sentence-transformers/all-MiniLM-L6-v2`, and maintains the Qdrant collection
  this agent queries. Not part of this repo; a hard runtime dependency.
- This project sits inside a broader self-hosted infrastructure migration; the
  agent is what makes that stack queryable end-to-end.

## License

MIT.
