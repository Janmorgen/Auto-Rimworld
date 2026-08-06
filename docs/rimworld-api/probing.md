# Getting an answer out of the install

Three sources, and they answer different questions. Reaching for the wrong one wastes an hour and
occasionally produces a confident wrong answer, which is worse.

## 1. Def XML — for *values*

Numbers live in XML, not in the assemblies. `manhunterOnDamageChance`, `baseHealthScale`, build
costs, nutrition — all of it. **[read]**

```
/run/media/deck/SD512/steamapps/common/RimWorld/Data/Core/Defs/
```

Animals are under `ThingDefs_Races/`, split by family rather than by name — muffalo and bison both
live in `Races_Animal_CowGroup.xml`. Grepping for `<defName>X</defName>` across the whole tree is
the reliable way in; guessing the filename is not.

Two traps, both hit while writing these notes:

- **Inheritance.** A def with `ParentName` inherits fields it does not restate, so a regex that
  reads one `<ThingDef>` block finds nothing for `Cougar` or `Hare` and reports absence where the
  real answer is "inherited from the parent". Absence in a single block is not absence.
- **Absent means default, not zero.** A missing `manhunterOnDamageChance` is the class default,
  which is not necessarily 0.

## 2. The metadata probe — for *signatures*

Type names, method signatures, enum members, whether a setter is public. Not values.

Lives at `$CLAUDE_JOB_DIR/tmp/apiprobe/`. It uses `MetadataLoadContext` with a
`PathAssemblyResolver` over `RimWorldLinux_Data/Managed`, with `coreAssemblyName: "mscorlib"` —
the last of which is not optional and is the reason a fresh attempt usually fails first. **[read]**

Reflection-over-metadata rather than loading the assembly, so it runs without a game process and
cannot execute anything. That is why it cannot answer a question about values: it never
constructs a `Def`.

What it is for, concretely: `Tradeable.CountToTransfer` looked like the obvious way to set a trade
quantity and its setter turned out to be inaccessible. The probe said so in a second; the
alternative was a build error and a guess about why.

## 3. A running colony — for *semantics*

Whether a field means what its name suggests. Neither of the above can tell you that, and it is
where the expensive mistakes are.

`kindDef.combatPower` is the standing example. Its type and value are trivially readable; that it
is a storyteller budget rather than a measure of fighting ability is only visible by comparing it
against outcomes across runs. See [animals.md](animals.md#combat-power-is-not-fighting-ability).

The mechanism is the chronicle: print the number beside the number it is being compared with, run
a colony, read what comes out. Three times this session that turned a plausible story into a
settled one, and once it refuted the story outright within a single restart.

## The order that works

1. Does [`../rimworld/`](../rimworld/index.md) say the mechanic exists? If not, check that it
   really does not — an entire missing system was found that way once, by reading
   [trading.md](../rimworld/trading.md) and finding nothing on the director's side of it.
2. Probe for the signature.
3. Read the def for the value.
4. If the answer depends on what a field *means*, instrument it and run a colony. Do not decide
   on it until the colony has answered.
