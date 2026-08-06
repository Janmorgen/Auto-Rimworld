"""
rimworld_graph_schema_v2.py
============================
A revision of rimworld_graph_schema.py, informed by twenty colonies (runs 132-151)
of a hand-written RimWorld director that died repeatedly and left a chronicle
explaining why each time.

Drop-in: RimWorldEncoder builds `input_proj` from NODE_FEATURE_DIMS and `convs`
from EDGE_TYPES generically, so this schema works with v1's encoder unchanged.

The original schema is a good ontology of the *systems* RimWorld has. What it
cannot express is the handful of distinctions those systems actually turn on --
and every one of the seven below is a distinction a real agent got wrong, in a
real colony, with a date and a body count. They are not hypothetical gaps.

The pattern across all seven is worth naming, because it is the argument for the
whole file: **not one of them was a reasoning error.** Each was a connection
nobody drew, a question asked at the wrong scope, or a measurement that had
quietly stopped tracking the thing it stood for. A network cannot learn around a
distinction its input does not encode, so these belong in the schema or nowhere.

Provenance is cited inline. `AutoColony/...` paths refer to the C# director.
"""

from __future__ import annotations

from typing import Dict, List, Set, Tuple

import networkx as nx


# =============================================================================
# 1. NODE TYPES
# =============================================================================
# Changes from v1 are marked. Widths are illustrative, as in v1 -- the point is
# which distinctions exist, not the arithmetic.

NODE_FEATURE_DIMS: Dict[str, int] = {
    # --- micro ---------------------------------------------------------------
    # v1: 41. Added the five fields that decide whether a colonist can actually
    # be sent, as opposed to existing. See §3.
    "colonist": 46,   # 12 skills + 12 passions + 5 needs + mood + 10 capacities
                       #   + is_downed, can_fight, health_above_retreat_threshold,
                       #   is_reachable, is_bleeding, ticks_to_bleedout

    "bodypart":  5,
    "animal":   10,
    "item":      5,
    "bill":      9,   # v1: 6. + work_remaining, eta_ticks, rate_is_measured  (§4)

    # --- meso ----------------------------------------------------------------
    # v1: 8 (wealth, beauty, space, cleanliness, impressiveness, temp_c,
    # is_freezer, tiles). The single most expensive omission in the file. See §1.
    "room": 14,   # + is_enclosed, touches_map_edge, psychologically_outdoors,
                   #   roofed_fraction, n_openings, n_animal_passable_openings

    # NEW (§1). A door and a gap are the same graph object with different
    # features, and that difference decides whether a manhunter walks in.
    "portal": 5,   # is_door, is_gap, passable_by_animal, hp_pct, is_blueprint

    "bench":  7,   # v1: 3. + has_fuel, fuel_pct, wants_refuel_now, is_operable  (§5)

    # NEW (§5). v1 has no generic building at all -- coolers, generators and
    # turrets simply do not exist in the graph.
    "building": 11,   # is_built, is_powered, has_fuel, fuel_pct, wants_refuel_now,
                       #   is_operable, hp_pct, is_roofed, is_outdoors,
                       #   unlocked_by_research_id, kind_id

    "zone": 9,   # v1: 6. + growing_days_remaining_this_year, next_window_start,
                  #   harvest_lands_before_frost  (§7)
    "faction": 4,

    # NEW (§7). Blight takes a whole crop at once, so a colony living off one
    # field is a single event from an empty larder -- which one zone-level
    # growth number cannot express.
    "crop": 6,   # growth_pct, days_to_harvest, yield_est, is_frost_sensitive,
                  #   blight_risk, sown_in_quadrum

    # --- transient -----------------------------------------------------------
    "trader":  4,
    "quest":   7,
    "caravan": 4,
    "guest":   3,

    # NEW (§2). v1 has no representation of an unharvested standing resource,
    # so it cannot express "there is wood, but not here".
    "resource_node": 7,   # kind_id, yield_est, dist_from_base, in_reach,
                           #   is_designated, regrows, times_harvested

    # NEW (§4). v1 has no blueprint or frame node: work in progress is invisible.
    "construction_site": 7,   # work_left, work_total, material_shortfall, is_frame,
                               #   role_served, ticks_standing, is_focus_room

    # --- macro ---------------------------------------------------------------
    "research": 10,   # v1: 7. + work_remaining, eta_ticks, rate_is_measured  (§4)
    "worldmap_tile": 5,
    "event": 5,

    "colony": 12,   # v1: 6. + roster_strength, fieldable_strength,           (§3)
                     #   expected_raid_points, gather_radius, reach_exhaustion,
                     #   medicine_days_remaining

    # --- virtual -------------------------------------------------------------
    "task_context": 3,

    # NEW (§4). One per graph. Every rate MEASURED, never looked up: a colony
    # whose builders are all drafted measures zero without being told that
    # drafting costs building.
    "pace": 6,   # research_pts_per_tick, construction_work_per_tick,
                  #   hauling_per_tick, cooking_per_tick, n_samples, ticks_since_reset

    # NEW (§6). The detector, not a feature. See precompute_precondition_features.
    "capability": 8,   # is_available, n_unmet_preconditions, is_bootstrap_blocked,
                        #   nearest_producer_hops, value_if_unlocked,
                        #   times_attempted, times_refused, ticks_blocked

    # NEW (§7). One per graph, from the game's own per-quadrum calculators.
    "season_forecast": 16,   # 4 quadrums x (mean_temp, can_grow_outdoors,
                              #   forage_nutrition, days_of_growing_window)
}


# =============================================================================
# 2. EDGE TYPES
# =============================================================================

EDGE_TYPES: List[Tuple[str, str, str]] = [
    # --- v1, unchanged -------------------------------------------------------
    ("colonist", "occupies", "room"),
    ("colonist", "has_part", "bodypart"),
    ("item", "stored_in", "room"),
    ("bench", "located_in", "room"),
    ("animal", "housed_in", "room"),
    ("room", "adjacent_to", "room"),          # GEOMETRIC. see connects_to below
    ("zone", "adjacent_to", "zone"),
    ("colonist", "assigned_to", "bench"),
    ("colonist", "assigned_to", "zone"),
    ("colonist", "handles", "animal"),
    ("colonist", "bonded_to", "animal"),
    ("colonist", "relates_to", "colonist"),
    ("colonist", "member_of", "caravan"),
    ("animal", "member_of", "caravan"),
    ("item", "loaded_on", "caravan"),
    ("bench", "runs", "bill"),
    ("bill", "consumes", "item"),
    ("bill", "produces", "item"),
    ("zone", "yields", "item"),
    ("animal", "produces", "item"),
    ("research", "prereq_of", "research"),
    ("research", "unlocks", "bench"),
    ("research", "unlocks", "item"),
    ("event", "threatens", "room"),
    ("event", "threatens", "colonist"),
    ("colonist", "targets", "event"),
    ("faction", "offers", "quest"),
    ("faction", "sends", "trader"),
    ("quest", "targets", "room"),
    ("quest", "targets", "guest"),
    ("quest", "targets", "worldmap_tile"),
    ("quest", "rewards", "item"),
    ("quest", "rewards_goodwill", "faction"),
    ("trader", "offers", "item"),
    ("caravan", "located_at", "worldmap_tile"),
    ("worldmap_tile", "adjacent_to", "worldmap_tile"),
    ("worldmap_tile", "owned_by", "faction"),
    ("colony", "context_for", "colonist"),
    ("colony", "context_for", "room"),
    ("colony", "context_for", "research"),
    ("colony", "context_for", "faction"),
    ("research", "reports_to", "task_context"),
    ("bill", "reports_to", "task_context"),
    ("quest", "reports_to", "task_context"),

    # --- §1 enclosure --------------------------------------------------------
    # connects_to is TOPOLOGICAL and is a different question from adjacent_to.
    # Two rooms sharing a wall are adjacent; they are connected only through a
    # portal. Only the second decides whether an animal can reach you.
    ("portal", "joins", "room"),
    ("room", "connects_to", "room"),

    # --- §2 reach vs the world ----------------------------------------------
    # TWO edges, deliberately, not one flag. "Can I get this today" and "does
    # this world contain any" want opposite answers, and collapsing them is the
    # fault that spent 1000 research points on tree-sowing inside a forest.
    ("colonist", "can_reach", "resource_node"),
    ("colony", "knows_of", "resource_node"),
    ("resource_node", "located_at", "worldmap_tile"),

    # --- §3 fieldable --------------------------------------------------------
    # Per-fight, not a global flag: a melee colonist is unfieldable against a
    # siege that shells from range and fine against a raid at the door.
    ("colonist", "fieldable_against", "event"),

    # --- §4 duration ---------------------------------------------------------
    ("pace", "times", "research"),
    ("pace", "times", "bill"),
    ("pace", "times", "construction_site"),
    ("construction_site", "builds", "room"),

    # --- §5 operability ------------------------------------------------------
    ("building", "located_in", "room"),
    ("item", "fuels", "building"),
    ("research", "unlocks", "building"),

    # --- §6 preconditions ----------------------------------------------------
    ("capability", "requires", "building"),
    ("capability", "requires", "room"),
    ("capability", "requires", "research"),
    ("capability", "requires", "item"),
    ("bill", "requires", "bench"),
    ("capability", "produces", "item"),
    ("capability", "produces", "colonist"),

    # --- §7 season -----------------------------------------------------------
    ("crop", "grows_in", "zone"),
    ("zone", "harvests_within", "season_forecast"),
]


def with_reverses(edge_types: List[Tuple[str, str, str]]) -> List[Tuple[str, str, str]]:
    """Unchanged from v1, including its fix: same-type edges get reverses too,
    because research-prereq_of-research is directional between instances."""
    out = list(edge_types)
    for src, rel, dst in edge_types:
        out.append((dst, f"rev_{rel}", src))
    return out


# =============================================================================
# 3. WHY EACH ADDITION EXISTS
# =============================================================================
#
# §1 ENCLOSURE -- room 8 -> 14, plus the portal node
#     v1's room cannot tell a sealed room from three walls and a gap. Both are
#     "a room" with a temp_c. This one omission killed colonies four ways:
#       - a manhunter walked into what the director called a refuge, because
#         the refuge test asked only whether the centre cell was ROOFED
#       - four beds counted as shelter while both colonists froze to death
#         outdoors beside them (run 142, hypothermia, day 41)
#       - four passive coolers at fifty wood each were placed in open air on a
#         map with no tree standing anywhere, and the heat complaint never moved
#       - the same distinction, asked correctly in one file and wrongly in three
#     The game answers it directly: !room.TouchesMapEdge && !room.PsychologicallyOutdoors.
#     is_enclosed and roofed_fraction stay SEPARATE: a walled courtyard keeps an
#     animal out and keeps no heat in; a roofed lean-to does the reverse.
#     is_temperature_controllable = is_enclosed and roofed_fraction == 1.
#
# §2 REACH vs THE WORLD -- resource_node, two edges, gather_radius on colony
#     The director works a 55-cell circle. Having felled every tree inside it by
#     day 10 it read "0 wood standing" -- true of the circle, false of the world
#     -- and committed its long-term goal to 1000 research points of tree-sowing
#     while a forest stood just outside. Its own fault table calls this class
#     "proxy for the real thing: a gather circle counted as the world".
#     gather_radius is a COLONY FEATURE THE POLICY CAN MOVE, not a constant
#     baked in at extraction. If it is fixed, the agent can never learn that the
#     answer to "no wood here" is sometimes a longer walk.
#
# §3 ROSTER vs FIELDABLE -- colonist +5, colony +2, fieldable_against
#     Strength counted colonists lying on the floor as fighters. Run 132 read
#     "strength 388" with three of four downed, charged, and lost all four to
#     blood loss. The fix was one function asked in both places -- the decision
#     and the drafting -- because when the filter lived only in the loop, the
#     decision above it was taken on a roster the loop was about to shrink.
#     Never collapse the two into one number; the GAP is the signal, and the
#     director prints it: "(52 of 148 fieldable -- 1 held back)".
#
# §4 DURATION -- pace, construction_site, eta_ticks, rate_is_measured
#     v1 has no notion of how long anything takes. This is not a detail: it is
#     the same argument rimworld_control_hierarchy.py makes for nesting its
#     managers, applied to the state rather than the policy. TopManager.gamma =
#     0.9995 gives a horizon near 2000 ticks; a 1000-point project at an early
#     colony's MEASURED rate is 40,000+. With eta_ticks present a manager can
#     notice its own discount cannot reach its own goal. Without it, it cannot.
#     rate_is_measured is a flag and not decoration -- an ETA from a measured
#     rate and one from a prior deserve different trust, the same distinction
#     v1 already draws by handing the encoder deterministic DAG facts.
#     Measured, never looked up: a rate table is wrong the moment the
#     researcher is hauling instead, and a measured rate folds in drafting,
#     mental breaks and skill for free, including reasons nobody thought of.
#
# §5 OPERABILITY -- building node, is_operable separate from is_built
#     A stove with no wood is not a stove. A bench nobody can use is not a
#     bench. The director was caught by this five times by its own count --
#     the unpowered turret, the kitchen with no stove, the research bench in a
#     colony where every colonist was incapable of research -- and every goal
#     that got it right had to be corrected: Power is satisfied by
#     workingGenerators > 0, not by a room named Power existing.
#     ("item","fuels","building") makes the woodpile-to-stove chain a path
#     rather than a coincidence.
#
# §6 A PRECONDITION NOTHING CREATES -- capability node + the precompute below
#     The most valuable addition, and the only one that is a DETECTOR rather
#     than a feature. v1 has research->prereq_of->research but nothing that can
#     say "X needs Y and nothing in this graph will ever produce Y".
#     Two deadlocks, both found only by reading logs:
#       - capture requires a prisoner bed; a prisoner bed is never built because
#         nothing wants one until there is a prisoner. Recruitment fired ZERO
#         times across fifteen colonies; all 23 arrivals were wanderers the game
#         handed over. The population never passed three, which is why one
#         casualty was always a third of the workforce.
#       - butchering requires a kitchen; a struggling colony never finishes one.
#         A colonist starved to death with 3.5 days of meat lying in the field.
#     ticks_blocked matters: blocked for fifteen colonies is a structural fault,
#     blocked for an hour is a queue, and only the counter tells them apart.
#
# §7 SEASONAL FORECAST -- season_forecast, crop, zone +3
#     The director's entire seasonal knowledge was one line:
#         growingSeasonNow = outdoorTemperature > 0 && < 58
#     A thermometer standing in for a year. It farmed through summer and starved
#     in fall, twice. Its fault table calls the class "present read as future".
#     The sharper finding is that the machinery ALREADY EXISTED and was pointed
#     only at animals: ForagePerQuadrum asks the game per quadrum for a pen, and
#     even handles seasonless biomes. Crops got a thermometer. The same question,
#     answered honestly for livestock and carelessly for the food supply.
#     harvest_lands_before_frost is an EDGE, not a boolean computed at
#     extraction, so a policy can reason behind it rather than being handed a
#     verdict.


# =============================================================================
# 4. PRECOMPUTED FACTS
# =============================================================================
# v1's argument -- compute what is deterministic and hand it over, so the
# network's limited depth is spent on what is genuinely uncertain -- is right,
# and generalises past reachability.


def precompute_dag_features(dag: nx.DiGraph, completed: Set[str]) -> Dict[str, Dict[str, float]]:
    """Unchanged from v1. topo_depth, is_reachable_now, hops_to_endgame."""
    topo = {n: i for i, n in enumerate(nx.topological_sort(dag))}
    reachable_now = {
        n for n in dag
        if n not in completed and all(p in completed for p in dag.predecessors(n))
    }
    sinks = [n for n in dag if dag.out_degree(n) == 0]

    def hops_to_nearest_sink(n: str) -> int:
        lengths = [nx.shortest_path_length(dag, n, s) for s in sinks if nx.has_path(dag, n, s)]
        return min(lengths) if lengths else -1

    return {
        n: {
            "topo_depth": float(topo[n]),
            "is_reachable_now": float(n in reachable_now),
            "hops_to_endgame": float(hops_to_nearest_sink(n)),
        }
        for n in dag
    }


def precompute_precondition_features(
    requires: Dict[str, Set[str]],
    produces: Dict[str, Set[str]],
    present: Set[str],
) -> Dict[str, Dict[str, float]]:
    """
    Which capabilities are blocked, and which are blocked by something NOTHING
    WILL EVER PRODUCE.

    requires: capability id -> the things it needs
    produces: capability id -> the things it yields
    present:  everything the colony already has

    `is_bootstrap_blocked` is the one that matters. A capability is bootstrap
    blocked when it is unavailable AND at least one of its missing preconditions
    is produced by nothing reachable -- so no amount of ordinary play will ever
    unblock it. That is a graph-algorithm fact, exactly the kind v1 argues
    should be handed to the encoder rather than reconstructed by message passing,
    and it is not something a policy can be expected to infer from never having
    seen a reward.

    Both real deadlocks are of this shape:
      capture   requires {prisoner_bed}; nothing produces prisoner_bed
      butchery  requires {butcher_table}; only a finished kitchen produces it,
                and a colony that cannot finish a kitchen never will

    An agent given this feature can at least be *surprised* by a permanently
    unreachable branch. An agent without it sees only a reward that never comes.
    """
    producible: Set[str] = set(present)
    changed = True
    while changed:                      # closure: what is reachable by chaining
        changed = False
        for cap, needs in requires.items():
            if needs <= producible:
                for made in produces.get(cap, ()):
                    if made not in producible:
                        producible.add(made)
                        changed = True

    out: Dict[str, Dict[str, float]] = {}
    for cap, needs in requires.items():
        missing = needs - present
        unproducible = {m for m in missing if m not in producible}
        out[cap] = {
            "is_available": float(not missing),
            "n_unmet_preconditions": float(len(missing)),
            "is_bootstrap_blocked": float(bool(unproducible)),
        }
    return out


# =============================================================================
# 5. WHAT THIS STILL CANNOT SEE
# =============================================================================
# The director's own rule is that the hour goes to whatever the colony lacks a
# sense for, so a schema should be honest about its own blind spots rather than
# read as complete.
#
# COVER. The reference calls fighting from behind cover while the enemy is in
#   the open "one of the most reliable defensive advantages in the game". There
#   is no positional feature anywhere here -- no cover_pct on colonist, no
#   is_chokepoint on room -- and the C# director has the same hole: its strength
#   model is offence x toughness with no positional term at all. A colony behind
#   sandbags and one standing in a field read identically. This is the largest
#   known omission in both designs.
#
# TRADE AS A CAPABILITY. v1 has a trader node and ("trader","offers","item"),
#   which is enough to model a transaction and not enough to notice a missing
#   one. A caravan stood on the map while a colony at zero medicine held 817
#   silver and no part of the director could connect them. medicine_days_remaining
#   is added to colony above; the §6 capability machinery would flag "buy
#   medicine" as available-and-unattempted. Neither is a substitute for an
#   action space that contains trading.
#
# SOCIAL STRUCTURE. relates_to exists as an edge with no features on it. Mood
#   collapse after a death was a recurring killer and the graph cannot say who
#   was bonded to whom, so it cannot anticipate which death breaks which
#   colonist.
#
# WHAT THE STORYTELLER IS ABOUT TO DO. event nodes are present tense. Raid size
#   scales with wealth and population, which means it is partly PREDICTABLE, and
#   nothing here expresses the forecast -- only expected_raid_points as a scalar
#   on colony. The wealth-hoarding penalty in the control hierarchy is trying to
#   price a consequence the state cannot represent.
