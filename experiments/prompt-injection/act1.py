#!/usr/bin/env python3
"""Act 1: reproduce the pre-fix agent file-tool boundary on the retained image.

Runs INSIDE odysseus-odysseus:pre-rebase-2026-09-12 with a synthetic, throwaway
DATA_DIR (ODYSSEUS_DATA_DIR) — no real data volume is mounted, so the files it
"exfiltrates" are ones this script just wrote with obviously fake contents.

It imports the real, unmodified pre-fix `_resolve_tool_path` — the exact function
that gated read_file / grep / glob / ls — and asks it to resolve the same
model-supplied paths a prompt-injected feed item would. Whatever that function
returns is a path the agent's read tools would then open. Nothing here is
reimplemented; the security boundary under test is the shipped one.
"""
import os
import sys

DATA = os.environ["ODYSSEUS_DATA_DIR"]
os.makedirs(os.path.join(DATA, ".ssh"), exist_ok=True)

# Synthetic stand-ins, real filenames. Contents are visibly fake.
open(os.path.join(DATA, "app.db"), "w").write("FAKE-sqlite: users, sessions, password hashes\n")
open(os.path.join(DATA, ".app_key"), "w").write("FAKE-fernet-key-AAAABBBBCCCCDDDD=\n")
open(os.path.join(DATA, ".ssh", "id_rsa"), "w").write("FAKE-PRIVATE-KEY\n")
open(os.path.join(DATA, ".env"), "w").write("FAKE_SECRET=hunter2\n")

sys.path.insert(0, "/app")
from src.tool_execution import _resolve_tool_path  # the shipped pre-fix gate

# What a prompt-injected feed item would ask the agent's read tools to open.
# These are the app's OWN state: the session/auth store and the key that
# decrypts every integration token in it.
targets = [
    ("data/app.db",   "session + auth store"),
    ("data/.app_key", "Fernet key: decrypts every stored token"),
    ("data/.ssh/id_rsa", "SSH private key (classic target)"),
    ("data/.env",     "dotenv secrets (classic target)"),
]

print("Pre-fix file-tool boundary — odysseus-odysseus:pre-rebase-2026-09-12")
print("DATA_DIR (synthetic): %s\n" % DATA)
print("  %-20s  %-9s  %s" % ("model-supplied path", "verdict", "what it is"))
print("  " + "-" * 74)
for raw, what in targets:
    # Feed an absolute path under the synthetic DATA_DIR, exactly as a resolved
    # relative path would land.
    abs_path = os.path.join(DATA, raw.split("/", 1)[1])
    try:
        resolved = _resolve_tool_path(abs_path)
        # The gate returned a path: the read tool would now open it. Prove it.
        head = open(resolved).read().strip()
        print("  %-20s  READABLE   %s" % (raw, what))
        print("  %-20s  └─ bytes returned to the model: %r" % ("", head))
    except ValueError as e:
        print("  %-20s  REJECTED   %s" % (raw, what))
        print("  %-20s  └─ %s" % ("", str(e).split(" (e.g.")[0]))
