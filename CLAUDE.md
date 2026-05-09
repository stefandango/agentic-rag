# AgenticRag

> Agentic RAG system (early scaffold).

**Stack:** .NET 10 console app, C# (nullable + implicit usings enabled).

## Commands

- Build: `dotnet build`
- Test: `dotnet test`
- Format: `dotnet format`
- Run: `dotnet run`

## Working with Claude

- Default mode is exploratory — sketch and iterate before locking direction.
- Lock direction in explicitly when ready (the user will say so).
- Be terse; match response length to the task.
- Confirm before irreversible actions (migrations, force-push, deletes).
- Run tests before claiming done; compiling is not working.

## Project notes

- Single-project repo (`AgenticRag.csproj`). No test project yet — `dotnet test` is a no-op until one is added.
- Targets `net10.0`. Nullable reference types are on; treat warnings seriously.
- `.claude/settings.json` already has a hook that blocks force-push to `main`.
