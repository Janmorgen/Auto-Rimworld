# Construction — blueprints, designations, terrain

Mechanics: [base-building.md](../rimworld/base-building.md) ·
[construction basics](../rimworld/base-building.md#construction-basics) ·
[room-types.md](../rimworld/room-types.md) ·
[room-attributes.md](../rimworld/room-attributes.md) ·
[materials.md](../rimworld/materials.md)

## Placing something to be built

```csharp
GenConstruct.CanPlaceBlueprintAt(def, cell, rot, map, false, null, null, stuff);   // returns AcceptanceReport
GenConstruct.PlaceBlueprintForBuild(def, cell, map, rot, Faction.OfPlayer, stuff);
GenConstruct.PlaceBlueprintForReinstall(building, target, map, rot, Faction.OfPlayer);
GenConstruct.CanBuildOnTerrain(def, cell, map, rot, null, null);
```
**[compiles]**

`CanPlaceBlueprintAt` refuses a contested cell, which is what lets several modules place
blueprints independently without coordinating: they coordinate through the world instead. Its
reason string is written for a player and is worth surfacing rather than paraphrasing.

**It does not check research.** A def the colony has not unlocked will pass and then never be
buildable. The tech gate is a separate question.

`CanBuildOnTerrain` is about the *terrain*, not about what is standing on it — a cell holding a
wall still passes.

## Designations

```csharp
map.designationManager.AddDesignation(new Designation(thing_or_cell, DesignationDefOf.Mine));
map.designationManager.DesignationOn(thing, def);      // thing-targeted
map.designationManager.DesignationAt(cell,  def);      // cell-targeted
map.designationManager.SpawnedDesignationsOfDef(def);  // copy before removing from it
```
**[compiles]**

Ones in use: `Mine`, `Hunt`, `HarvestPlant`, `Uninstall`.

Mining is cell-targeted, hunting is thing-targeted, and the two guard against duplicates with
different calls. Two modules both designating mining cannot double-mark a cell — but they draw on
the same miner-hours and neither sizes its budget against the other's queue, which is a real
contention with no cell collision to reveal it.

## Reading the ground

```csharp
cell.GetEdifice(map)          // the wall, rock or building standing here — null if clear
cell.GetTerrain(map)
terrain.passability == Traversability.Impassable
terrain.affordances.Contains(TerrainAffordanceDefOf.Heavy)
cell.InBounds(map)
cell.Fogged(map)
GenRadial.RadialCellsAround(origin, radius, true)
map.listerThings / map.mapPawns.AllPawnsSpawned
```
**[compiles]**

**`GetEdifice(map) != null` is not the same as "unbuildable here".** It is true of natural rock,
of the colony's own walls, and of ancient ruins — three situations that want opposite answers. A
buildability measure that counts any edifice as bad scores a complete standing structure, which
is the best possible room site, as the worst (#56).

**Ancient ruin walls are faction-less.** So a check of the form
`edifice.Faction != null && edifice.Faction != player` does not reject them, and a site containing
ruins reaches the scorer rather than being refused before it. **[read]**

## Work remaining

```csharp
IConstructible.TotalMaterialCost()
frame.WorkLeft
blueprint.WorkToBuild
```
**[compiles]** — summing these over a footprint gives a real ETA for a room, and differencing that
sum over time gives a construction rate that folds in drafting, mood breaks and builder skill for
free, without a stat lookup.

## Costs worth remembering

Read via the probe rather than the wiki **[read]**:

| building | notes |
|---|---|
| Wood-fired generator | needs fuel hauled to it; useless where nothing burns |
| Wind turbine | same research, same cost, ~2.3× output, needs open ground |
| Solar generator | no fuel, output varies with light |

Generation trade-offs are in [power.md](../rimworld/power.md#power-generation). The wind turbine
matters on treeless maps, where the wood-fired generator has no fuel chain at all (#49) — but
`PlaceWorker_WindTurbine` demands clear ground ahead of it, so siting is not interchangeable.
