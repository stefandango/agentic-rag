#!/usr/bin/env python3
"""Act 2a: the same four questions as Act 1, against today's fixed build.

Runs INSIDE odysseus-odysseus:latest with a synthetic throwaway DATA_DIR, no
network, no real data mounted — identical envelope to Act 1. It imports the
shipped post-fix `_resolve_tool_path` (same name, same signature as the pre-fix
one) and asks it to resolve the exact paths Act 1 used.

A fifth probe reads a file the agent is *supposed* to reach, under
agent_workspace, to show the fix confines the tools rather than breaking them.
"""
import os
import sys

DATA = os.environ["ODYSSEUS_DATA_DIR"]
os.makedirs(os.path.join(DATA, ".ssh"), exist_ok=True)
os.makedirs(os.path.join(DATA, "agent_workspace"), exist_ok=True)

open(os.path.join(DATA, "app.db"), "w").write("FAKE-sqlite: users, sessions, password hashes\n")
open(os.path.join(DATA, ".app_key"), "w").write("FAKE-fernet-key-AAAABBBBCCCCDDDD=\n")
open(os.path.join(DATA, ".ssh", "id_rsa"), "w").write("FAKE-PRIVATE-KEY\n")
open(os.path.join(DATA, ".env"), "w").write("FAKE_SECRET=hunter2\n")
open(os.path.join(DATA, "agent_workspace", "scratch.md"), "w").write("legit scratch note\n")

sys.path.insert(0, "/app")
from src.tool_execution import _resolve_tool_path  # the shipped post-fix gate

targets = [
    ("data/app.db",   "session + auth store"),
    ("data/.app_key", "Fernet key: decrypts every stored token"),
    ("data/.ssh/id_rsa", "SSH private key (classic target)"),
    ("data/.env",     "dotenv secrets (classic target)"),
    ("data/agent_workspace/scratch.md", "the agent's own workspace (legitimate)"),
]

print("Post-fix file-tool boundary — odysseus-odysseus:latest")
print("DATA_DIR (synthetic): %s\n" % DATA)
print("  %-34s  %-9s  %s" % ("model-supplied path", "verdict", "what it is"))
print("  " + "-" * 82)
for raw, what in targets:
    abs_path = os.path.join(DATA, raw.split("/", 1)[1])
    try:
        resolved = _resolve_tool_path(abs_path)
        head = open(resolved).read().strip()
        print("  %-34s  READABLE   %s" % (raw, what))
        print("  %-34s  └─ bytes returned to the model: %r" % ("", head))
    except ValueError as e:
        reason = str(e).split(" (e.g.")[0]
        print("  %-34s  REJECTED   %s" % (raw, what))
        print("  %-34s  └─ %s" % ("", reason))
