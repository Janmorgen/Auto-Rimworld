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
| Item claiming | Unforbids what the colony may use; locks items down under threat |
| Production | Standing bills on every work table, sized to stock targets |
| Resource gathering | Chopping, mining and hunting designations against stock targets |
| Research | Keeps a project selected at all times |
| Defense | Builds turrets in peacetime, drafts and rallies colonists under threat |
| Colonist policy | Medical care level, self-tending, hostility response, prisoner handling |
| Incidents | Answers the quest, refugee and trade decisions the game raises |

The director never spawns anything. It places blueprints and designations and sets
priorities, exactly as a player would, and the colonists' own job system does the work.

Item claiming deserves a note because it is invisible until it bites. RimWorld marks a great
deal as forbidden — drops, raider loot, ruins, and a scenario's starting resources — and a
colony that never unforbids them stands next to its own steel and food unable to touch either.
That looks like the director being incompetent when it is merely blocked. It claims within a
learnable radius, and under threat a cautious strategy pushes items outside the home area back
to forbidden so haulers do not walk into a firefight. It never claims anything under fog:
unforbidding the contents of an unopened ancient danger is how a colony finds a sealed room
full of mechanoids.

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

## The noise problem, and the two things that address it

A colony's score is far noisier than the difference between two decent strategies. The
offline tests measure this directly: at the production gene count the plain sequential search
is already flat once score noise reaches ~0.02, and without a correction it actively
*degrades* — roughly half of all promotions are luck, so the incumbent random-walks away from
any optimum. Three mechanisms exist to deal with that.

**An acceptance margin.** The engine estimates score noise from re-measurements of the same
strategy — the only place two scores describe an identical genome — and requires a challenger
to clear the incumbent by a multiple of it. This trades a slower climb for not going
backwards.

**Training mode (seed-locked paired trials).** Instead of averaging the noise away, remove it.
Each round snapshots the game, then replays the same stretch of time once per candidate,
reloading the snapshot and re-seeding RimWorld's RNG identically each time. Every candidate
meets the same raids, weather and traders, so the shared luck that dominates a colony score
cancels out of the comparison. Measured against the sequential search at the same noise level,
this is the only lever that still makes ground.

It is not free, and the tests pin that too: a round of four candidates buys one generation for
four evaluations, so with clean scores it *loses* to plain sequential search. It pays off only
when noise is what is holding the search back. The game visibly reloads between trials, so it
is off by default and refuses to run in permadeath.

## Keeping the game running

RimWorld pauses for events it thinks you should see — a research project finishing, a raid
landing. A paused game issues no ticks, so an autonomous director stalls there indefinitely.

This is subtler than it looks. `GameComponentTick` does not run while paused, so a director
that only acted on ticks *could never unpause itself*. Time control therefore runs on
`GameComponentUpdate`, which fires every frame regardless — the only place a pause can be
both observed and undone.

Two different things stall the game and need different handling. A time speed of `Paused` is
fixed by setting it back. A modal popup sets `TickManager.ForcePaused`, where setting the
speed does nothing at all until the window is closed.

**Whose pause it was decides what happens.** A pause the *game* raised is the director's to
clear. A pause *you* pressed is an instruction to stop, and is left strictly alone until you
resume — otherwise the mod fights you every time you want to look at your own colony. The two
are told apart by whether a letter landed in the moment the pause appeared, which is what an
event pause looks like and a keypress does not.

Both kinds are handled after a short delay rather than instantly, so letters stay readable.
Only event popups are ever closed, and while the options screen or any mod's settings are
open the mod does not touch anything. A popup it does not recognise is named in the log rather
than force-closed, so a stuck game is diagnosable instead of looking like a hang.

It deliberately does **not** change `Prefs.AutomaticPauseMode` or `Prefs.PauseOnLoad`. Those
are persistent player settings; a mod that rewrites them leaves your game altered if it is
ever removed. Correcting the speed after the fact leaves nothing behind.

**Learning from you.** While automation is off, the mod watches how you run the colony — how
widely and how urgently you assign each work type, what stock levels you hold, how many beds
you put in a room, what you grow and research — and fits a strategy to it. Hand the colony
over and the search starts from your habits instead of from defaults, which matters because a
colony only affords tens of epochs against ~50 genes. Work weights are normalised against
their own mean, since the gene controls relative emphasis rather than an absolute level.

## Building

Requires the .NET SDK. No RimWorld install is needed to compile — the build pulls public
reference assemblies from NuGet.

```bash
cd Source/AutoColony
dotnet build
```

This writes `Assemblies/AutoColony.dll`.

The learning layer has no RimWorld dependencies beyond a handful of persistence interfaces,
so it runs — and is tested — outside the game:

```bash
cd Tests/AutoColony.Tests
dotnet test
```

Those tests compile the *real* production sources against a small `Verse` shim rather than a
copy, so they exercise the shipped algorithms. They cover the search (convergence on a
synthetic landscape, behaviour under noise, paired-trial gain), the bandits (regret and
tracking a switch), the fitness function (monotonicity in deaths, food and mood), genome
bounds and XML round-tripping, the archive, and the player model. This is the only place the
optimiser can realistically be exercised at all — in-game an epoch costs about an hour of real
time.

Then copy or symlink the repository root into your RimWorld `Mods` folder:

```bash
ln -s "$PWD" "$HOME/.steam/steam/steamapps/common/RimWorld/Mods/Auto-Rimworld"
```

The mod folder layout is the standard one:

```
Auto-Rimworld/
├── About/             About.xml, Manifest.xml, Preview.png
├── Assemblies/        AutoColony.dll (build output)
├── Defs/              MainButtonDef for the status tab
├── Source/AutoColony  Source and .csproj
└── Tests/             Offline tests (not shipped to RimWorld)
```

## Using it

Enable the mod, start or load a colony, and it takes over. The **auto-colony** tab on the
bottom bar shows the current epoch score and its breakdown, the search state (best score,
mutation step, generation, how many improvements have been accepted), a per-epoch score
history, what each subsystem last did, and which genes have moved away from their defaults.

While automation is off the tab instead shows how far the mod has got towards a strategy
fitted to your own play, and what it has inferred so far.

Mod settings let you set epoch length, turn cross-colony learning and learning-from-you on or
off, enable training rounds and set their candidate count, switch individual subsystems off to
keep that part of the game for yourself, and erase the archive.

A reasonable way to use it: play normally for a week or two of game time so the mod can watch
you, then switch automation on — it starts from your habits. Turn on training mode if you want
it to actually improve on them in reasonable wall-clock time.

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
- **The learning layer never touches `Verse.Rand`.** It carries its own splitmix64 PRNG.
  Drawing from RimWorld's global stream would advance it by an amount depending on how many
  mutations happened to occur, perturbing every later world roll relative to an unmodded game
  and making two runs from the same save incomparable — which the trial harness depends on.

## The event record

Colony failures are almost never one thing. A raider arrives, nobody is drafted, a fire
starts, the fire is not fought, the survivors are short of food, and the response to being
short of food kills the rest. Reading only the end state invites blaming whichever cause is
most visible — a frozen corpse says "cold" and says nothing about the raider an hour earlier.

So the director keeps a chronicle at `<save folder>/AutoColony/chronicle.log`: events in
order, with colony vitals interleaved every two in-game hours, surviving the session. Reading
backwards from a death gives the chain rather than the last link.

```
day 0 00h  HUNT     0.0 days of food - hunting Hare (33) x2, Emu (70) x2, Boomalope (80) x3;
                    too dangerous: Rhinoceros (270), Cougar (120)
day 0 02h  INCIDENT answered 'Inspired surgery: Rabbit' with 'close'
day 0 08h  VITALS   colonists 3 (down 0, breaking 0)  mood 0.74  health 1.00  food 8.6d
```

Threats, fires and deaths are written through immediately rather than buffered, since those
are exactly the entries a crash would otherwise take with it. The last entries are also shown
in the auto-colony tab.

## Known limits

- **The search is sample-starved.** ~50 genes against tens of epochs per playthrough. The
  measurements behind this live in the test suite; dimensionality reduction is the obvious next
  step, and tuning the mutation rate is measurably *not* — 0.3 is already near-optimal.
- **Seed locking decays.** Trials start identical, but once colonies diverge they consume RNG
  draws at different rates and the worlds drift apart. Early epoch time is the comparable part.
- **No power grid and no freezer.** The planner places an electric stove but never generators,
  batteries or conduits, and food spoils without cooling. Probably the largest gameplay gap.
- Untouched: caravans and trade, animal taming, apparel/weapon assignment, surgery scheduling,
  multi-map colonies, defensive geometry.

## Status

Runs in RimWorld 1.6.4871. 56 offline tests cover the learning layer, and the mod has been
exercised in-game on a generated test colony with no exceptions logged.

Confirmed working in-game:

- Loads and initialises (`Strategy space: 52 genes (20 work types)`), and the **auto-colony**
  tab appears in the bottom bar.
- Base siting, work priorities, colonist policy, research selection, zone creation and resource
  designations all execute against a live map.
- Epochs close and score (0.566 on a fresh desert colony), and the evolution engine advances.
- **Training mode completes a full round**: snapshot → trial → reload → trial → pick winner →
  reload. The static session state survives the `Game` teardown a reload causes, and both
  trials ran from identical state. Two rounds ran back to back.

Not yet exercised in-game: production bills (needs work tables built), defense (needs a raid),
and incident answering (needs a choice letter). Long-horizon behaviour — whether the colony is
still healthy after a few in-game years — is untested and is the obvious next thing to watch.
