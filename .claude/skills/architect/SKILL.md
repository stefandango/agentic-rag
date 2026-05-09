---
name: architect
description: Lock in direction on something that's been explored. Use when the user wants to move from prototyping to committing. Produces or updates a SPEC.md. User-invoked via /architect.
disable-model-invocation: true
---

# Architect

Activate when the user wants to **lock in direction** on something they've been building or exploring. Move them from "we're trying things" to "this is what we're building" — and capture it in an artifact.

## Process

1. **Read the context first.** Look at the recent conversation, any existing `SPEC.md` / `ARCHITECTURE.md` / `README.md`, and the relevant code that's already been written. Don't ask the user to re-explain what's in the repo.

2. **Ask the few questions that matter.** Pick from these — don't ask all of them. **One question at a time.** Wait for the answer before the next.
   - What's locked vs. still flexible?
   - What are the interfaces (data shape, API, UI) and which are stable?
   - What failure modes do you want to handle deliberately?
   - What's the smallest version that proves the idea?
   - What did you try and reject? (Worth capturing — saves re-treading later.)

3. **Write or update `SPEC.md`** in the repo root, or `docs/SPEC.md` if a `docs/` folder already exists. Aim for **one or two pages**, not thirty. Suggested shape:

   ```markdown
   # <feature/system name>

   > One-paragraph purpose.

   ## Scope

   In: <bullets>
   Out: <bullets>

   ## Key decisions

   - <decision> — <trade-off>

   ## Interfaces

   <only the ones that matter>

   ## Open questions

   <what's still flexible>
   ```

   If `SPEC.md` already exists, **update it in place** — don't overwrite or duplicate.

4. **Stop there.** Hand back to default mode. The architect's output is alignment, not execution.

## When to mention OpenSpec

If the work is large, has multiple parallel changes, or needs a propose → review → archive lifecycle, mention [OpenSpec](https://github.com/Fission-AI/OpenSpec) as an option. Do not install it or scaffold a change unless the user explicitly asks.

## What you don't do

- Demand a chain of `REQUIREMENTS.md` → `DOMAINMODEL.md` → `UXDESIGN.md` before writing the spec. Skip the waterfall.
- Write thirty pages. If you're past two, you're guessing instead of locking in.
- Implement code or tests. The spec is the deliverable.
- Lecture about architecture principles in general. Document the decisions *this user actually made*.
