---
description: One-shot project onboarding — detect stack, write a project-specific CLAUDE.md, optionally add hooks
allowed-tools: Read, Write, Edit, Glob, Grep, Bash(ls:*), Bash(cat:*), Bash(jq:*), Bash(pwd:*), Bash(git:*), Bash(test:*), Bash(find:*), Bash(head:*)
---

# Project context

Working directory:
!`pwd`

Existing CLAUDE.md (first 100 lines, if present):
!`test -f CLAUDE.md && head -100 CLAUDE.md || echo "(none)"`

Existing .claude config:
!`ls -la .claude/ 2>/dev/null || echo "(none)"`

Manifests present:
!`ls package.json Cargo.toml go.mod pyproject.toml Gemfile pom.xml build.gradle setup.py Makefile *.csproj *.sln 2>/dev/null || echo "(none)"`

package.json (if present):
!`test -f package.json && jq '{name, type, scripts, packageManager, engines}' package.json 2>/dev/null || true`

Lockfiles (signals the package manager):
!`ls package-lock.json pnpm-lock.yaml yarn.lock bun.lockb 2>/dev/null || true`

Cargo.toml header (if present):
!`test -f Cargo.toml && head -15 Cargo.toml || true`

go.mod (if present):
!`test -f go.mod && head -5 go.mod || true`

pyproject.toml header (if present):
!`test -f pyproject.toml && head -30 pyproject.toml || true`

tsconfig.json strict-related fields (if present):
!`test -f tsconfig.json && jq '.compilerOptions | {strict, noUncheckedIndexedAccess, noImplicitAny}' tsconfig.json 2>/dev/null || true`

Git state:
!`git rev-parse --is-inside-work-tree 2>/dev/null && git remote get-url origin 2>/dev/null || echo "(not a git repo)"`

CI config:
!`ls .github/workflows/ .gitlab-ci.yml .circleci/ 2>/dev/null || echo "(none)"`

---

# Your job

Set up Claude Code for this project. The output is a **lean, project-specific `CLAUDE.md`** (target ~30-80 lines) plus optional hooks. Skip ceremony.

## Step 1 — Detect the project

From the context above, identify:

- **Language and framework** (e.g. TypeScript+React, Go HTTP service, Rust CLI, Python+FastAPI, .NET 8 API)
- **Package manager** (lockfiles tell you: `package-lock.json` → npm, `pnpm-lock.yaml` → pnpm, `yarn.lock` → yarn, `bun.lockb` → bun; cargo, go modules, uv/poetry/pip, dotnet, etc.)
- **Test runner, linter, formatter** (from `devDependencies`, `[tool.*]` sections, CI config, etc.)
- **Actual** build / test / lint / dev / typecheck commands — read them, don't guess
- **Repo layout** — monorepo workspaces? notable top-level dirs?
- **Anything unusual** — non-default toolchain, custom scripts, conventions a stranger wouldn't expect

If something important is missing or ambiguous, **ask the user one question at a time**. Do not guess.

## Step 2 — Handle the existing CLAUDE.md

If `CLAUDE.md` already exists:

1. Read it.
2. If it's the v3 placeholder skeleton (contains `{{PROJECT_NAME}}` etc.), replace it directly.
3. If it's user-written content, **do not overwrite without asking**. Show the user what you'd write and ask whether to:
   - **Merge** — keep their notes and project-specific sections; fill in the detected commands
   - **Replace** — back up their content to `CLAUDE.md.bak` first
   - **Skip** — leave their CLAUDE.md alone

## Step 3 — Write the project CLAUDE.md

Use this shape. **Aim for under 80 lines total.** For every line, ask "would Claude make a mistake without this?" If no, cut it.

```markdown
# <project name>

> <one-line purpose>

**Stack:** <one line — language, framework, key tools>

## Commands

- Build: `<actual cmd>`
- Test: `<actual cmd>`
- Lint: `<actual cmd>`
- Typecheck: `<actual cmd>`   <!-- only if the project has a separate typecheck step -->
- Dev: `<actual cmd>`

## Working with Claude

<Keep these unless they conflict with how the project is run. Trim duplicates.>

- Default mode is exploratory — sketch and iterate before locking direction.
- Lock direction in explicitly when ready (the user will say so).
- Be terse; match response length to the task.
- Confirm before irreversible actions (migrations, force-push, deletes).
- Run tests before claiming done; compiling is not working.

## Project notes

<Anything Claude should always know about this repo specifically:
 - Monorepo layout / workspace map (only if non-obvious from `ls`)
 - Domain quirks or gotchas
 - Where docs live, where decisions are recorded
 - Non-default conventions
 - "Do not touch X without asking"
Skip the section entirely if nothing fits — empty sections are noise.>
```

**Do NOT include:**
- Code style rules — that's the linter's job
- Verbose docstrings, workflow diagrams, role tables
- An `/init`-style wall of auto-generated tech-stack prose
- Anything already obvious from the README

## Step 4 — Offer optional setup (one yes/no question at a time)

After CLAUDE.md is written, ask the user separately:

### 4a. Format-on-edit hook?

Only if a formatter is detected (prettier, gofmt, black, rustfmt, dotnet format, etc.).

> "I can add a `PostToolUse` hook that runs `<formatter>` automatically after Write/Edit. This eliminates a permission prompt every time. Add it?"

If yes, write or update `.claude/settings.json`:

```json
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "Write|Edit",
        "hooks": [
          {
            "type": "command",
            "command": "FILE=$(jq -r '.tool_input.file_path // empty'); <FORMATTER_CMD_HERE> 2>/dev/null; exit 0"
          }
        ]
      }
    ]
  }
}
```

Replace `<FORMATTER_CMD_HERE>` with the project's actual formatter (e.g. `if [[ \"$FILE\" == *.ts || \"$FILE\" == *.tsx ]]; then npx prettier --write \"$FILE\"; fi`). If `.claude/settings.json` already exists, **merge** — don't overwrite.

### 4b. Project `/pr` command?

Only if `git remote get-url origin` returned a GitHub URL.

> "I can generate a project-specific `/pr` command that runs typecheck → test → lint, then opens a PR via `gh`. Add it?"

If yes, write `.claude/commands/pr.md` using the actual detected commands.

### 4c. `.gitignore` entry?

If `.gitignore` exists and doesn't already ignore `.claude/settings.local.json`:

> "Add `.claude/settings.local.json` to `.gitignore` so per-machine permissions don't get committed?"

## Step 5 — Summary

Print a short summary:

- Files written or modified (with paths)
- Anything skipped and why
- One concrete next step the user might take

## What you must NOT do

- Run any `git` write command (`init`, `add`, `commit`, `push`, branch creation, tag creation)
- Install dependencies or modify source code
- Add or remove MCP servers, or modify global settings (`~/.claude/`)
- Auto-generate a 200-line CLAUDE.md — if you can't keep it under ~80 lines, ask the user what to cut
- Write any plan, design, or architecture doc — this is setup, not architecture
