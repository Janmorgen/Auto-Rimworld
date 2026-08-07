#!/usr/bin/env bash
#
# Start a fresh Auto-Rimworld colony safely.
#
#   ./restart-run.sh <runNumber> [--keep-archive]
#
# Written after an rm -rf destroyed the mod config and the strategy archive: the old
# procedure copied config *out of* the previous run's folder, which is one of the folders
# the procedure deletes, so deleting in the wrong order silently produced a vanilla game
# with no mod loaded. Three rules follow from that:
#
#   1. Config comes from config-template/, which nothing here ever deletes.
#   2. Nothing is deleted until the new run has been proven to work.
#   3. Every path passed to rm is checked to be under JD and non-empty first.
#
set -uo pipefail

JD="/home/deck/.claude/jobs/18bdf7fd/tmp"
RIM="/run/media/deck/SD512/steamapps/common/RimWorld"
TEMPLATE="$JD/config-template"
SAVE="$JD/rw-savedata"

RUN="${1:-}"
[[ -z "$RUN" ]] && { echo "usage: restart-run.sh <runNumber> [--keep-archive]"; exit 2; }
KEEP_ARCHIVE=0
[[ "${2:-}" == "--keep-archive" ]] && KEEP_ARCHIVE=1

# A guarded rm. Refuses anything that is not a non-empty path underneath JD, which is the
# check that was missing when this went wrong.
safe_rm() {
    local target="$1"
    [[ -z "$target" ]]      && { echo "REFUSED: empty path"; return 1; }
    [[ "$target" == "$JD" ]] && { echo "REFUSED: will not delete JD itself"; return 1; }
    case "$target" in
        "$JD"/?*) ;;
        *) echo "REFUSED: $target is not under $JD"; return 1 ;;
    esac
    rm -rf -- "$target"
}

[[ -f "$TEMPLATE/ModsConfig.xml" ]] || { echo "FATAL: no config template at $TEMPLATE"; exit 1; }

echo "== stopping any running colony =="
pkill -f "savedatafolder=$SAVE"
sleep 6

# Archive the outgoing run before anything is removed, so a failure here loses nothing.
if [[ -d "$SAVE" ]]; then
    echo "== archiving previous run as run${RUN}-prev =="
    safe_rm "$JD/run${RUN}-prev-savedata"
    mv "$SAVE" "$JD/run${RUN}-prev-savedata"
    [[ -f "$JD/rimworld.log" ]] && mv "$JD/rimworld.log" "$JD/run${RUN}-prev-rimworld.log"

    # The strategy archive is the only irreplaceable thing in there.
    if [[ -f "$JD/run${RUN}-prev-savedata/AutoColony/strategy_archive.xml" ]]; then
        cp "$JD/run${RUN}-prev-savedata/AutoColony/strategy_archive.xml" \
           "$JD/strategy_archive-before-run${RUN}.xml"
        echo "   archive backed up to strategy_archive-before-run${RUN}.xml"
    fi
fi

echo "== building the fresh save folder from the template =="
mkdir -p "$SAVE/Config" "$SAVE/AutoColony"
cp "$TEMPLATE"/*.xml "$SAVE/Config/"

if [[ "$KEEP_ARCHIVE" == "1" && -f "$JD/rw-savedata-archive-seed.xml" ]]; then
    cp "$JD/rw-savedata-archive-seed.xml" "$SAVE/AutoColony/strategy_archive.xml"
    echo "   seeded the strategy archive (scores predate the Room quality term)"
fi

echo "== launching run $RUN =="
cd "$RIM" || exit 1
LC_ALL=C AUTOCOLONY_SCENARIO="${AUTOCOLONY_SCENARIO:-}" \
    nohup ./RimWorldLinux -savedatafolder="$SAVE" -quicktest \
    -logfile "$JD/rimworld.log" > "$JD/rimworld.stdout" 2>&1 &
# Wait for the chronicle rather than guessing how long a map takes to generate.
#
# This was a flat "sleep 55" and on 2026-08-06 run 175 wrote its first line at ~75s, so the
# check reported "DID NOT START CLEANLY" about a game that was starting perfectly well and
# went on to run fine. A false failure is worse than a slow check: it invites re-restarting a
# healthy colony, which is the one action guaranteed to destroy it.
#
# Map generation is the variable part and it scales with map size and biome, so poll.
for _ in $(seq 1 40); do
  [[ -s "$SAVE/AutoColony/chronicle.log" ]] && break
  sleep 5
done

# Prove it works before reporting success. "RUNNING" alone was what hid the vanilla launch:
# the process was up and the mod was simply absent.
fail=0
pgrep -f "savedatafolder=$SAVE" >/dev/null || { echo "FAIL: process not running"; fail=1; }
grep -qi "autocolony" "$JD/rimworld.log" || { echo "FAIL: mod not loaded"; fail=1; }
[[ -f "$SAVE/AutoColony/chronicle.log" ]] || { echo "FAIL: no chronicle"; fail=1; }

exc=$(grep -c "Exception" "$JD/rimworld.log" 2>/dev/null | head -1); exc=${exc:-0}
[[ "$exc" != "0" ]] && { echo "WARN: $exc exceptions at startup"; }

if [[ "$fail" == "1" ]]; then
    echo "RUN $RUN DID NOT START CLEANLY — previous run is intact at run${RUN}-prev-savedata"
    exit 1
fi

echo "RUN $RUN OK — exceptions: $exc, build $(git -C /home/deck/Documents/projects/auto-rimworld rev-parse --short HEAD)"
sed -n '2p' "$SAVE/AutoColony/chronicle.log"
