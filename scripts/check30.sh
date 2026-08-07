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
#
# Full screen, then cropped to the game window by its own geometry.
#
# This used `spectacle -a`, which captures the ACTIVE window. That is the game only
# while nothing else has focus, and on 2026-08-06 it silently returned a file manager
# showing a directory listing — a check that would have reported on the colony from a
# picture of some filenames. The whole reason step 1 of the checklist exists is that the
# screen catches what the logs cannot, and an instrument that quietly photographs the
# wrong thing is worse than none: it still produces an image, and the image still looks
# like an answer.
#
# Deliberately does NOT focus the game first. xdotool windowactivate would make this
# reliable and would also yank focus away from whoever is using the machine, every
# thirty minutes, for ever.
WID=$(DISPLAY=:0 xdotool search --name "RimWorld" 2>/dev/null | head -1)
DISPLAY=:0 spectacle -b -n -f -o "$SHOT.full.png" >/dev/null 2>&1
sleep 3

if [ -n "$WID" ]; then
  GEO=$(DISPLAY=:0 xdotool getwindowgeometry "$WID" 2>/dev/null)
  POS=$(echo "$GEO" | grep -oE 'Position: [0-9]+,[0-9]+' | grep -oE '[0-9]+,[0-9]+')
  DIM=$(echo "$GEO" | grep -oE 'Geometry: [0-9]+x[0-9]+' | grep -oE '[0-9]+x[0-9]+')
  if [ -n "$POS" ] && [ -n "$DIM" ]; then
    ffmpeg -loglevel error -y -i "$SHOT.full.png" \
      -vf "crop=${DIM%x*}:${DIM#*x}:${POS%,*}:${POS#*,}" "$SHOT" >/dev/null 2>&1
  fi
fi

# If anything above failed, the full screen is still an honest picture — small, but not
# a picture of something else. Never leave a stale $SHOT in place to be read as current.
[ -s "$SHOT" ] || cp "$SHOT.full.png" "$SHOT" 2>/dev/null

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
          | grep -vE "working towards|work is leaning|VITALS" \
          | sort | uniq -c | sort -rn | awk '$1>=4{printf "%dx %s; ", $1, substr($0, index($0,$2))}' \
          | cut -c1-200)
[ -z "$repeats" ] && repeats="none"

died=$(grep -c "died of\|gone from the colony" "$CH" 2>/dev/null); died=${died:-0}
taken=$(grep -c "gone from the colony" "$CH" 2>/dev/null); taken=${taken:-0}
causes=$(grep -o "died of .*" "$CH" 2>/dev/null | sed 's/died of //' | sort | uniq -c | tr '\n' ';' | tr -s ' ')

excmod=$(grep -c "AutoColony.*Exception" "$LOG" 2>/dev/null); excmod=${excmod:-0}
warns=$(grep -c "^Warning:" "$LOG" 2>/dev/null); warns=${warns:-0}

# Also a cumulative count rather than a state — kept, but named honestly, so it
# is not read as "rooms standing right now". The vitals line carries the live
# figures; this is a tally of rooms that have ever come good.
rooms=$(grep -c "room is working" "$CH" 2>/dev/null); rooms=${rooms:-0}
# "pen is" also matches "the open is elective" in threat lines. Count what was
# fenced and what the game agrees is closed, which are the two facts worth having.
sited=$(grep -c "fencing a" "$CH" 2>/dev/null); sited=${sited:-0}

# The pen's CURRENT verdict, not how many times it has ever been closed.
#
# This was `grep -c "the pen is closed"`, a tally of every time the line ever
# appeared — and the director prints that line only when the answer *changes*.
# Run 142 closed its pen on day 0 and lost it again on day 4; the game was
# showing "Pen not enclosed" on screen while this read "1 closed", because one
# was the count and the other was the state. A count of events reported as a
# state is the same fault this script exists to catch in the director, and it
# went unnoticed here for the life of the script.
#
# So: take the last verdict of either kind and say what it was.
penline=$(grep -E "the pen is closed|the pen has a marker standing" "$CH" 2>/dev/null | tail -1)
case "$penline" in
    *"the pen is closed"*) penstate="closed" ;;
    *"marker standing"*)   penstate="OPEN" ;;
    *)                     penstate="none" ;;
esac
pens="$sited sited/$penstate now"
lean=$(grep "work is leaning" "$CH" 2>/dev/null | tail -1 | sed 's/.*leaning — //; s/ *\[[A-Z][a-z]*,[^]]*\]//')
season=$(grep -o "\[\(Spring\|Summer\|Fall\|Winter\),[^]]*\]" "$CH" 2>/dev/null | tail -1)
vitals=$(grep "colonists [0-9]" "$CH" 2>/dev/null | tail -1)

# The focus, stamped with when it was said.
#
# "working towards" is logged when the focus CHANGES, and vitals every couple of in-game
# hours, so the last of each can be far apart — and this line used to set them side by side
# with nothing to say so. On 2026-08-06 that produced "0 of 1 beds are inside a room" beside
# "beds 4 (0 sheltered)" and cost a real stretch of chasing a bed count that disagreed with
# itself. It did not. One number was hours older than the other.
#
# Same failure as the screenshot that photographed a file manager, in a different instrument
# on the same afternoon: an output that is individually true and jointly misleading. Cheaper
# to print the timestamp than to re-learn this.
focusline=$(grep "working towards" "$CH" 2>/dev/null | tail -1)
focusat=$(echo "$focusline" | grep -oE "^day [0-9]+ [0-9]+h")
nowat=$(echo "$vitals" | grep -oE "^day [0-9]+ [0-9]+h")
focus=$(echo "$focusline" | sed 's/.*towards — //')
[ -n "$focusat" ] && [ "$focusat" != "$nowat" ] && focus="$focus  (said at $focusat)"
death=$(grep "COLONY LOST\|final score" "$CH" 2>/dev/null | tail -1)

# Say "too early" rather than printing a row of empty fields.
#
# A check run seconds after a restart finds no VITALS line yet and used to emit
# "lean:  | focus:  |  |" — which reads exactly like a colony that has stopped doing
# anything, and sent me to check whether the game had died. It had not. Fifth time today
# an instrument has produced output that was true and looked like something else.
if [ -z "$vitals" ]; then
  lines=$(wc -l < "$CH" 2>/dev/null || echo 0)
  vitals="no vitals yet — chronicle has $lines lines, so the colony has started and has not
reached its first vitals pass"
fi

echo "CHECK30| $running | $frame | died: $died taken: $taken | repeats: $repeats | causes: $causes | excMOD: $excmod warns: $warns | roomsEver: $rooms pen: $pens | lean: $lean $season | focus: $focus | $vitals | $death"

# ---------------------------------------------------------------- goal.md
# Recited every check, READ FROM THE FILE rather than copied here.
#
# A second copy of the rules is a copy that drifts, which is the exact fault this
# project keeps finding in the director — one question with two answers. So this
# pulls the loop priority and the checklist straight out of goal.md, and if that
# file changes the reminder changes with it. If goal.md is missing, say so loudly
# rather than quietly reciting nothing.
GOAL=/home/deck/Documents/projects/auto-rimworld/goal.md
if [ -f "$GOAL" ]; then
    echo "--- goal.md ---"
    # Loop priority: the one-line rule under section 3.
    sed -n '/^## 3\. Loop priority/,/^---/p' "$GOAL" | grep '^> ' \
        | sed -E 's/^> /priority: /; s/\*\*//g'
    # The checklist itself, numbered lines under section 6.
    sed -n '/^## 6\. Every-loop checklist/,/^---/p' "$GOAL" \
        | grep -E '^[0-9]+\.' \
        | sed -E 's/\*\*//g; s/`//g; s/[[:space:]]+/ /g'
else
    echo "--- goal.md MISSING at $GOAL — the checklist cannot be recited ---"
fi
