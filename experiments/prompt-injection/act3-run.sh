#!/bin/bash
# Act 3: the same injection through the production report scripts.
#
# Starts fake_miniflux.py (one benign entry, one hostile one whose body tells the
# reader to read the app key, POST it out, and delete the database), points the
# real dawn-brief.py and weekly-review.py at it in --dry-run, and shows where the
# injection ends up. The two report scripts live in the homelab repo (see
# README); point REPORTS_REPO at a checkout of it. They need their own config
# and a reachable Kuma/weather — trusted infrastructure, not an injection vector.
#
# Nothing is written (--dry-run) and the only untrusted input is the feed this
# script controls. Everything else (weather, Kuma) is trusted infrastructure, not
# an injection vector.
set -u
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="${REPORTS_REPO:-$(cd "$SCRIPT_DIR/../.." && pwd)}"
FEED="http://127.0.0.1:8899"

python3 "$SCRIPT_DIR/fake_miniflux.py" & SRV=$!
trap 'kill $SRV 2>/dev/null' EXIT
sleep 2

echo "############################################################"
echo "# Act 3a — dawn-brief.py (the daily: NO model, NO tools)"
echo "############################################################"
MINIFLUX_URL="$FEED" MINIFLUX_TOKEN=dummy "$REPO/scripts/dawn-brief.py" --dry-run 2>&1 \
    | sed -n '/## Worth reading/,/would push/p'

echo
echo "############################################################"
echo "# Act 3b — weekly-review.py (the weekly: ONE model call, NO tools)"
echo "############################################################"
MINIFLUX_URL="$FEED" MINIFLUX_TOKEN=dummy "$REPO/scripts/weekly-review.py" --dry-run 2>&1 \
    | sed -n '/## From the feeds/,/would push/p'
