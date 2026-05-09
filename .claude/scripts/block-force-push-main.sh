#!/usr/bin/env bash
# PreToolUse hook: block `git push --force` (and variants) targeting main/master.
# Reads the tool call as JSON from stdin and exits 2 to block when the rule fires.
#
# Wired up in .claude/settings.json. Edit or remove that entry to disable.

set -euo pipefail

# Degrade gracefully if jq is missing — never block on tool absence.
command -v jq >/dev/null 2>&1 || exit 0

PAYLOAD=$(cat)
CMD=$(printf '%s' "$PAYLOAD" | jq -r '.tool_input.command // empty')

# Not a `git push`? Allow.
printf '%s' "$CMD" | grep -qE '\bgit[[:space:]]+push\b' || exit 0

# Not a force push? Allow. Catches: --force, --force-with-lease,
# --force-if-includes, and -f as a standalone short flag.
printf '%s' "$CMD" | grep -qE '(--force\b|--force-with-lease\b|--force-if-includes\b|(^|[[:space:]])-f([[:space:]]|$))' || exit 0

# Decide whether main/master is the target — either explicit in the command
# or implicit via the current branch.
TARGETS_PROTECTED=false

if printf '%s' "$CMD" | grep -qE '(\b|:)(main|master)\b'; then
  TARGETS_PROTECTED=true
fi

CURRENT_BRANCH=$(git branch --show-current 2>/dev/null || echo "")
if [[ "$CURRENT_BRANCH" == "main" || "$CURRENT_BRANCH" == "master" ]]; then
  TARGETS_PROTECTED=true
fi

if [[ "$TARGETS_PROTECTED" == "true" ]]; then
  cat >&2 <<EOF
BLOCKED: force-push to main/master detected.

  Command: $CMD
  Current branch: ${CURRENT_BRANCH:-(unknown)}

If this is intentional, run the command yourself in a terminal — Claude Code
will not execute it. The hook lives at .claude/scripts/block-force-push-main.sh
if you want to inspect or customize the rule.
EOF
  exit 2
fi

exit 0
