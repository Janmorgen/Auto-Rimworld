# Auto-Rimworld

A RimWorld 1.6 mod that removes the player from the game. An autonomous director runs the
colony end to end, and searches for a better way to run it as it goes.

## What it actually does

The director is a perceive–decide–act loop wrapped in a learning loop.

**Acting.** Nine subsystems cover the things a player spends their time on:

| Subsystem | What it handles |
| --- | --- |
| Work priorities | Per-colonist work assignment from skill, passion, and current colony need |
| Base planner | Picks a site, reserves rooms along a corridor, queues walls, doors and furniture |
| Zones | Growing zones on fertile soil, stockpiles in the storage room |
| Production | Standing bills on every work table, sized to stock targets |
| Resource gathering | Chopping, mining and hunting designations against stock targets |
| Research | Keeps a project selected at all times |
| Defense | Builds turrets in peacetime, drafts and rallies colonists under threat |
| Colonist policy | Medical care level, self-tending, hostility response, prisoner handling |
| Incidents | Answers the quest, refugee and trade decisions the game raises |

The director never spawns anything. It places blueprints and designations and sets
priorities, exactly as a player would, and the colonists' own job system does the work.

**Learning.** Every management decision is parameterised. Roughly fifty genes — food reserve
targets, per-work-type weights, room size, beds per room, defence spending as a fraction of
wealth, mining aggression, prisoner recruitment bias, and so on — form one *strategy*.

Each epoch (10 in-game days by default) the colony is scored on how it actually fared, and
a **(1+1) evolution strategy** with Rechenberg step-size adaptation mutates the strategy:
a challenger that beats the incumbent is promoted and the mutation step widens; one that
loses is discarded and the step narrows. The search anneals from coarse exploration to fine
tuning on its own.

Choices that are discrete rather than continuous — which research project, which room to
build next, which crop to sow — go to **discounted UCB1 bandits** instead. Each choice made
during an epoch is credited with that epoch's score. Discounting matters because RimWorld is
non-stationary: what was worth doing in year one is often worthless by year five.

**Scoring.** Fitness weights survival (0.30), growth (0.20), food security (0.15), mood
(0.12), health (0.08), research (0.07), infrastructure (0.05) and defence (0.03). Mood and
health are time-averaged across the epoch and food security uses the *worst* reserve reached,
so a colony cannot score well by looking healthy only on the last day. Wealth uses log-growth
so the number stays comparable as the colony scales.

The scoring weights are deliberately **not** genes. If the strategy could tune its own
yardstick, the optimiser would learn to redefine success instead of playing better.

**Learning that outlives the save.** The best strategy found is written to
`<save folder>/AutoColony/strategy_archive.xml`, keyed by biome and difficulty. A new colony
seeds from the best strategy previously learned for a comparable situation, and its first
epoch re-measures that strategy under current conditions before mutating away from it. This
is what makes the mod improve across playthroughs rather than relearning the same lessons in
every colony.

## Building

Requires the .NET SDK. No RimWorld install is needed to compile — the build pulls public
reference assemblies from NuGet.

```bash
cd Source/AutoColony
dotnet build
```

This writes `Assemblies/AutoColony.dll`. Then copy or symlink the repository root into your
RimWorld `Mods` folder:

```bash
ln -s "$PWD" "$HOME/.steam/steam/steamapps/common/RimWorld/Mods/Auto-Rimworld"
```

The mod folder layout is the standard one:

```
Auto-Rimworld/
├── About/            About.xml, Manifest.xml, Preview.png
├── Assemblies/       AutoColony.dll (build output)
├── Defs/             MainButtonDef for the status tab
└── Source/AutoColony Source and .csproj
```

## Using it

Enable the mod, start or load a colony, and it takes over. The **auto-colony** tab on the
bottom bar shows the current epoch score and its breakdown, the search state (best score,
mutation step, generation, how many improvements have been accepted), a per-epoch score
history, what each subsystem last did, and which genes have moved away from their defaults.

Mod settings let you set epoch length, turn cross-colony learning off, switch individual
subsystems off to keep that part of the game for yourself, and erase the archive.

## Design notes

- **Fault isolation.** Every subsystem runs inside a try/catch and is disabled after five
  failures rather than taking the game down. This code runs unattended for hours.
- **Tick budget.** At most one subsystem runs per tick, on a round-robin schedule, so the
  director's per-tick cost stays flat as subsystems are added.
- **Def tolerance.** Every def is resolved by name and allowed to be missing, so a missing
  DLC or an unusual mod list disables one feature instead of throwing.
- **Player bills are left alone.** Auto-generated production bills are tagged; anything you
  wrote by hand is never edited or deleted.
- **No Harmony.** Nothing needed patching — incidents are answered through the public letter
  stack API — so there are no patch conflicts with other mods.

## Status

The mod compiles cleanly against RimWorld 1.6.4871 reference assemblies, and every API call
was verified against those assemblies rather than written from memory. It has **not** been
run inside RimWorld yet — there is no game install on the machine it was written on — so
treat the first playthrough as a shakedown and check the log for `[AutoColony]` warnings.
