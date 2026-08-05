#!/usr/bin/env bash
# Run 30-minute check on the driving colony.
# Emits one CHECK30 line and always captures a screenshot beside it.
# Screenshot is not optional, and neither is opening it: capture, then READ the image.
# Reporting vitals off a screenshot nobody looked at is the failure this guards.
# Original note: the user reads these to correct my misreadings,
# and twice now a stale text reading has been caught only by the picture.
JD=/home/deck/.claude/jobs/18bdf7fd/tmp
CH="$JD/rw-savedata/AutoColony/chronicle.log"
LOG="$JD/rimworld.log"
SHOT="$JD/watch.png"

# --- screenshot first, so it is contemporaneous with the vitals below ---
DISPLAY=:0 spectacle -b -n -a -o "$SHOT" >/dev/null 2>&1
sleep 3

running=$(pgrep -f RimWorldLinux >/dev/null && echo running || echo DEAD)

# frame health: compare against the PREVIOUS check, 30 min ago. Comparing two
# reads seconds apart always says stalled — a game hour is longer than that.
STATE="$JD/.lastday"
now=$(grep -o "^day [0-9]* [0-9]*h" "$CH" 2>/dev/null | tail -1)
prev=$(cat "$STATE" 2>/dev/null)
echo "$now" > "$STATE"
if [ -z "$prev" ]; then frame="frame first"
elif [ "$now" = "$prev" ]; then frame="frame STALLED at $now"
else frame="frame ok"; fi

# Repeated chronicle lines: the signature of a remedy that is not fixing anything.
# 28 identical "saguaro cactus plot sown" lines sat in the log for four checks before
# anybody noticed they were the same line. A count is cheaper than my attention.
#
# Recent lines only. The first version counted the whole run, so a loop that closed four
# in-game days ago still reported "6x AddTable" for ever and read as an active fault —
# which it did on run 120, and I nearly chased it. A detector that cannot go quiet is the
# same failure as a remedy that cannot clear its own complaint.
# Windowed by game day rather than by line count: early in a run the whole log is short,
# so "last 400 lines" is still the whole history. Two in-game days is long enough to catch
# a loop firing every six hours and short enough to go quiet once it stops.
curday=$(grep -oE "^day [0-9]+" "$CH" 2>/dev/null | tail -1 | grep -oE "[0-9]+")
curday=${curday:-0}
from=$((curday-2)); [ "$from" -lt 0 ] && from=0
repeats=$(awk -v from="$from" '
    match($0, /^day ([0-9]+) /, m) { if (m[1]+0 >= from) { sub(/^day [0-9]+ [0-9]+h[ ]*/,""); print } }
  ' "$CH" 2>/dev/null \
          | grep -E "^(BUILD|ECONOMY|HEALTH)" \
          | sort | uniq -c | sort -rn | awk '$1>=4{printf "%dx %s; ", $1, substr($0, index($0,$2))}' \
          | cut -c1-200)
[ -z "$repeats" ] && repeats="none"

died=$(grep -c "died of\|gone from the colony" "$CH" 2>/dev/null); died=${died:-0}
taken=$(grep -c "gone from the colony" "$CH" 2>/dev/null); taken=${taken:-0}
causes=$(grep -o "died of .*" "$CH" 2>/dev/null | sed 's/died of //' | sort | uniq -c | tr '\n' ';' | tr -s ' ')

excmod=$(grep -c "AutoColony.*Exception" "$LOG" 2>/dev/null); excmod=${excmod:-0}
warns=$(grep -c "^Warning:" "$LOG" 2>/dev/null); warns=${warns:-0}

rooms=$(grep -c "room is working" "$CH" 2>/dev/null); rooms=${rooms:-0}
# "pen is" also matches "the open is elective" in threat lines. Count what was
# fenced and what the game agrees is closed, which are the two facts worth having.
sited=$(grep -c "fencing a" "$CH" 2>/dev/null); sited=${sited:-0}
closed=$(grep -c "the pen is closed" "$CH" 2>/dev/null); closed=${closed:-0}
pens="$sited sited/$closed closed"
lean=$(grep "work is leaning" "$CH" 2>/dev/null | tail -1 | sed 's/.*leaning — //; s/ *\[[A-Z][a-z]*,[^]]*\]//')
season=$(grep -o "\[\(Spring\|Summer\|Fall\|Winter\),[^]]*\]" "$CH" 2>/dev/null | tail -1)
vitals=$(grep "colonists [0-9]" "$CH" 2>/dev/null | tail -1)
focus=$(grep "working towards" "$CH" 2>/dev/null | tail -1 | sed 's/.*towards — //')
death=$(grep "COLONY LOST\|final score" "$CH" 2>/dev/null | tail -1)

echo "CHECK30| $running | $frame | died: $died taken: $taken | repeats: $repeats | causes: $causes | excMOD: $excmod warns: $warns | rooms: $rooms pens: $pens | lean: $lean $season | focus: $focus | $vitals | $death"
