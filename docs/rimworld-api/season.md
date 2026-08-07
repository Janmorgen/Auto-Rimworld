# Season — the calendar and the growing window

Mechanics: [biomes.md](../rimworld/biomes.md) ·
[what biome actually controls](../rimworld/biomes.md#what-biome-actually-controls) ·
[plants.md](../rimworld/plants.md) ·
[rimworld-core-mechanics.md](../rimworld/rimworld-core-mechanics.md#world--environment)

## Calendar constants

```csharp
GenDate.TicksPerDay        // 60000
GenDate.DaysPerTwelfth     // 5
GenDate.TwelfthsPerYear    // 12
```
**[read]** — so a year is 60 days, a quadrum is 15, a twelfth is 5.

```csharp
GenLocalDate.Twelfth(map)        // which twelfth it is here, now
GenLocalDate.DayOfTwelfth(map)
```
**[compiles]**

## Forecasting the growing season

The important one, because it is the difference between a snapshot and a forecast:

```csharp
GenTemperature.TwelfthsInAverageTemperatureRange(PlanetTile, float minTemp, float maxTemp)
```
**[compiles]** — returns the twelfths of the year whose *average* temperature falls in the range.
Ask it with the plant growth bounds and it gives back the growing season for that tile, from which
"days of growing left" and "barren days ahead" follow by counting forward from the current twelfth.

**This is the fix for reading today's thermometer as the year.** A colony that asks whether crops
grow *right now* farms happily through summer and starves in fall; it needs to know when the
window closes, not whether it is currently open. That fault has a row of its own in `goal.md`'s
table — *present read as future*.

Note the same question was already being asked honestly elsewhere: the pen forage estimate walks
the quadrums and asks the game per quadrum, for animals. Same question, answered properly for a
pen and by a thermometer for a field, in one codebase. Worth checking for that shape.

## Temperature

```csharp
map.mapTemperature.OutdoorTemp
GenTemperature.SeasonalShiftAmplitudeAt(tile)
```
**[compiles]**

Biome determines the growing window, the temperature band and what forages —
[what biome actually controls](../rimworld/biomes.md#what-biome-actually-controls) is the
one-page version. A map with `0d barren` is a permanent-growing biome and is a **real answer**,
not a missing one: it must not be treated as an unread forecast, or every seasonless map silently
falls back to a default.

That distinction is worth stating in the chronicle. A forecast that quietly stops working looks
identical to one saying the fields never stop, and this project has been caught by exactly that
in its own instruments more than once.

## Practical spread observed

Maps seen while driving colonies, as `growing days left` / `barren days ahead`:

| | |
|---|---|
| Permanent summer | 60 / 0 |
| Temperate | 34 / 15, 23 / 25, 25 / 25 |
| Ice sheet | 0 / 60 |

A target computed as `barrenDays × margin` needs the margin to be a **ratio near 1.3**, not a
count of days. Passing a days-valued gene into that parameter produced a demand for 150 days of
food on a 25-day winter (#58, and [disagreements.md](disagreements.md#units)).

## Temperature the colony has not reached yet

```csharp
GenTemperature.AverageTemperatureAtTileForTwelfth(map.Tile, twelfth);
GenTemperature.TwelfthsInAverageTemperatureRange(map.Tile, min, max);
GenLocalDate.Twelfth(map);
GenLocalDate.DayOfTwelfth(map);
```
**[compiles]**

The second one drives the growing forecast; the first answers the same question for cold, and
walking all twelve gives both *how cold it gets* and *how many days until then*.

**Two numbers, not one.** A remedy that takes days cannot be steered by a flag — the deadline is
half the answer. This is the same distinction the bleeding clock draws in
[health.md](health.md#the-bleeding-clock): a count of who is hurt cannot say whether help arrives
in time.

**Watched failing, run 195.** `ComfortableMin` is 16C. The colony sat under a thermometer all
summer, so its warm-clothes branch was false every pass, went true for the first time on the
morning the outdoor temperature touched 15C, and started its first parka on day 21 — fourteen
growing days from a fifteen-day barren quadrum. **[live, run 195]**

```
day 21 00h  VITALS  colonists 4  food 5.3d  15C      → alert: "Need warm clothes"
            14d growing left, then 15d barren
```

Nothing was broken. Every number was correct and the decision was reading the wrong one, which
is the `present read as future` row of goal.md's fault table and the same mistake
`growingSeasonNow` made about fields before the forecast replaced it. Worth stating because the
fix is not new perception: the call was already in `ColonyState`, serving a different consumer.
