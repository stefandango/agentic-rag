---
description: Build, test, format-check, then open a PR via `gh`.
---

Run the project's pre-PR checks, then open a pull request.

1. **Build** — `dotnet build` (must succeed; treat warnings as warnings, not failures, but surface anything new).
2. **Test** — `dotnet test`. If no test project exists yet, note that and skip.
3. **Format check** — `dotnet format --verify-no-changes`. If it fails, run `dotnet format` to fix, then re-stage.
4. **Open PR** — use `gh pr create` against `main`. Title should be short (<70 chars). Body: `## Summary` (1–3 bullets) and `## Test plan` (checklist). Use a HEREDOC for the body.

Stop and ask the user if any step fails for a non-obvious reason. Do not push to `main` directly.
