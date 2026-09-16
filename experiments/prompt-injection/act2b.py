#!/usr/bin/env python3
"""Act 2b: the second defence — refusing to ACT after untrusted content.

Act 2a showed the read tools can no longer reach the app's secrets. This shows
the other half: even a tool that is allowed to read a feed cannot then take a
consequential action in the same run, because the run is marked as influenced by
untrusted content.

Runs INSIDE odysseus-odysseus:latest, no network, no data. It drives the shipped
`ToolRunSecurityContext` — the exact server-owned integrity state the agent loop
carries — through the sequence a scheduled brief actually follows:

    1. fresh run: the tools it needs are allowed
    2. it fetches a feed (untrusted content enters the run)
    3. the SAME tools are now refused

The refusal is what the scheduler surfaces as "Scheduled task paused safely".
"""
import sys

sys.path.insert(0, "/app")
from src.tool_capabilities import ToolRunSecurityContext

ctx = ToolRunSecurityContext()

# The two calls the brief must make to deliver: fetch/write via api_call, and
# write the note. (write_note is an MCP tool the classifier does not know, so it
# is treated as high-impact — deliberately.)
def verdict(tool):
    d = ctx.decision_for(tool)
    return ("ALLOWED", "") if d.allowed else ("REFUSED", getattr(d, "reason", "") or "")

print("Post-fix action gate — odysseus-odysseus:latest\n")

print("1. Fresh run, nothing untrusted seen yet:")
for tool in ("api_call", "write_note"):
    v, _ = verdict(tool)
    print("     %-12s %s" % (tool, v))

print("\n2. The run fetches a feed. api_call returns article text — untrusted:")
# A successful api_call result carrying body text: this is what arms the gate.
ctx.observe_tool_result("api_call", {"stdout": '{"entries":[{"title":"...","content":"...injected..."}]}',
                                     "exit_code": 0})
print("     external_untrusted_context_seen = %s" % ctx.external_untrusted_context_seen)

print("\n3. The SAME calls, now that untrusted content is in the run:")
for tool in ("api_call", "write_note"):
    v, reason = verdict(tool)
    print("     %-12s %s" % (tool, v))
    if reason:
        print("     %-12s └─ %s" % ("", reason))

print("\nThe first fetch is what arms the gate — which is why re-ordering the")
print("prompt cannot rescue an unattended run: by the time it has read anything")
print("worth acting on, acting is already refused.")
