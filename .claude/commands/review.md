---
description: Review the current branch's diff against main using the reviewer subagent (isolated context)
---

Invoke the `reviewer` subagent to review the current branch's diff. The subagent runs in isolated context — it reads the entire diff without polluting this session — and returns a structured verdict (APPROVE / CHANGES REQUESTED / BLOCKERS) with `file:line` findings.

If the user passed extra context as arguments (e.g. `/review focus on tests` or `/review the auth changes`), forward it to the subagent as additional guidance.
