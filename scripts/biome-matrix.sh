#!/usr/bin/env bash
#
# Run the director across a fixed set of biomes, one after another, with the seed held
# constant per biome so the same build always meets the same map.
#
#   ./biome-matrix.sh <label> [minutesPerBiome]
#
# The point is comparability. Every run this project drove before this started somewhere
# different, so a colony doing better told you nothing about whether the build had improved
# or the map had been kinder. Holding biome and seed fixed and varying only the build makes
# the comparison mean something.
#
# The biome list is deliberately spread across the axes that have actually broken things:
#
#   TemperateForest  the easy case, and the one every earlier run kept landing on
#   BorealForest     short growing season, cold snaps
#   AridShrubland    thin wood, hot — where run 110's fuel chain came apart
#   Desert           almost no wood, no growing season worth the name
#   Tundra           no wood at all, everything indoors
#
# Results land in $OUT/<label>/<biome>/ with the chronicle, the log and a screenshot, plus a
# one-line summary per biome in $OUT/<label>/summary.txt.

set -uo pipefail

JD="/home/deck/.claude/jobs/18bdf7fd/tmp"
RIM="/run/media/deck/SD512/steamapps/common/RimWorld"
TEMPLATE="$JD/config-template"
OUT="$JD/matrix"

LABEL="${1:-}"
[[ -z "$LABEL" ]] && { echo "usage: biome-matrix.sh <label> [minutesPerBiome]"; exit 2; }
MINUTES="${2:-20}"

# Seed per biome, fixed for all time. Changing one of these invalidates comparison with every
# earlier matrix, so they are written here rather than generated.
declare -A SEED=(
  [TemperateForest]="tf-001"
  [BorealForest]="bf-001"
  [AridShrubland]="as-001"
  [Desert]="de-001"
  [Tundra]="tu-001"
)
ORDER=(TemperateForest BorealForest AridShrubland Desert Tundra)

mkdir -p "$OUT/$LABEL"
SUMMARY="$OUT/$LABEL/summary.txt"
BUILD=$(git -C /home/deck/Documents/projects/auto-rimworld rev-parse --short HEAD)
{
  echo "matrix $LABEL — build $BUILD — ${MINUTES}min per biome"
  echo "started $(date '+%Y-%m-%d %H:%M')"
  echo
} > "$SUMMARY"

for BIOME in "${ORDER[@]}"; do
    SEEDVAL="${SEED[$BIOME]}"
    DIR="$OUT/$LABEL/$BIOME"
    SAVE="$JD/rw-matrix"

    echo "== $BIOME (seed $SEEDVAL) =="
    pkill -f "savedatafolder=$SAVE" 2>/dev/null
    sleep 4

    rm -rf "${SAVE:?}"
    mkdir -p "$SAVE/Config" "$SAVE/AutoColony" "$DIR"
    cp "$TEMPLATE"/*.xml "$SAVE/Config/"

    cd "$RIM" || exit 1
    LC_ALL=C AUTOCOLONY_BIOME="$BIOME" AUTOCOLONY_SEED="$SEEDVAL" \
        nohup ./RimWorldLinux -savedatafolder="$SAVE" -quicktest \
        -logfile "$DIR/rimworld.log" > "$DIR/stdout.txt" 2>&1 &

    # Wait for the map rather than a fixed sleep: generation time varies by biome.
    n=0
    until grep -q "starting tile pinned" "$DIR/rimworld.log" 2>/dev/null || [ $n -gt 60 ]; do
        sleep 5; n=$((n+1))
    done
    if ! grep -q "starting tile pinned" "$DIR/rimworld.log" 2>/dev/null; then
        echo "$BIOME  FAILED TO START (no tile pinned)" >> "$SUMMARY"
        continue
    fi

    TILE=$(grep -o "pinned to .*" "$DIR/rimworld.log" | head -1)

    # Let it play.
    END=$(( $(date +%s) + MINUTES * 60 ))
    while [ "$(date +%s)" -lt "$END" ]; do sleep 30; done

    DISPLAY=:0 spectacle -b -n -a -o "$DIR/screen.png" >/dev/null 2>&1
    sleep 3
    cp "$SAVE/AutoColony/chronicle.log" "$DIR/chronicle.log" 2>/dev/null

    CH="$DIR/chronicle.log"
    LAST=$(grep VITALS "$CH" 2>/dev/null | grep -v upkeep | tail -1)
    DAY=$(echo "$LAST" | grep -oE "^day [0-9]+" | head -1)
    DIED=$(grep -c "died of" "$CH" 2>/dev/null); DIED=${DIED:-0}
    TAKEN=$(grep -c "gone from the colony" "$CH" 2>/dev/null); TAKEN=${TAKEN:-0}
    EXC=$(grep -c "AutoColony.*Exception" "$DIR/rimworld.log" 2>/dev/null); EXC=${EXC:-0}
    DRY=$(grep -oE "[0-9]+ DRY[^ ]*" "$CH" 2>/dev/null | tail -1)
    SCORE=$(grep -oE "epoch [0-9]+ scored [0-9.]+" "$CH" 2>/dev/null | tail -1)

    printf "%-16s %-22s %-10s died=%s taken=%s exc=%s  %s  %s\n" \
        "$BIOME" "$TILE" "${DAY:-day ?}" "$DIED" "$TAKEN" "$EXC" "${DRY:-}" "${SCORE:-}" >> "$SUMMARY"

    pkill -f "savedatafolder=$SAVE" 2>/dev/null
    sleep 4
done

echo >> "$SUMMARY"
echo "finished $(date '+%Y-%m-%d %H:%M')" >> "$SUMMARY"
cat "$SUMMARY"
