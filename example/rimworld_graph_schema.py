"""
rimworld_graph_schema.py
=========================
Heterogeneous graph schema + encoder for a RimWorld colony-management agent.
Built on PyTorch Geometric (HeteroData + HeteroConv).

Node/edge types map 1:1 onto the game systems already documented in the
reference notes:
    colonist/mood/health/prosthetics -> colonists.md, mood.md, health.md
    research                        -> research.md (the prereq table IS the DAG)
    crafting/materials/rooms         -> crafting.md, materials.md, room-attributes.md
    world/threats                    -> biomes.md, combat.md, storyteller.md, events.md
    diplomacy/economy                 -> factions.md, trading.md, questing.md

Dimensions below are illustrative scaffolding, not tuned values -- the real
numbers depend on the actual state extractor pulled from the game. The point
is the *structure*: which node types exist, how they connect, and where
raw-feature dims give way to learned embeddings for categorical/variable
fields (traits, work-type, item category, quest reward branches, etc.).

Long-chain handling (added after the first pass): a virtual `task_context`
node gives every Research/Bill/Quest node an O(1)-hop path to a shared
summary, `precompute_dag_features` hands the encoder deterministic facts
(reachability, topological depth) instead of asking message passing to
reconstruct them, and `TaskDAGEncoder` runs a separate, deeper pass over just
the Task-DAG subgraph so it isn't bottlenecked by the shallow conv depth the
much bigger colony graph is tuned for.
"""

from __future__ import annotations

from enum import Enum, auto
from typing import Dict, List, Optional, Tuple

import networkx as nx
import torch
from torch import nn, Tensor
from torch_geometric.data import HeteroData
from torch_geometric.nn import GATv2Conv, HeteroConv


# =============================================================================
# 1. NODE TYPES -- raw continuous feature width per type
# =============================================================================
# Categorical fields (traits, backstory, work-type, item category, quest
# "shape", trader inventory-focus, biome id, storyteller id, ...) are NOT
# counted here -- they're looked up through nn.Embedding tables inside
# RimWorldEncoder and concatenated on before the raw Linear projection.

NODE_FEATURE_DIMS: Dict[str, int] = {
    # --- micro tier ----------------------------------------------------
    "colonist": 41,   # 12 skills + 12 passions + 5 needs + mood + 10 capacities + is_downed
    "bodypart":  5,   # efficiency, is_vital, bionic_tier(0-4), pain, bleed_rate
    "animal":   10,   # tameness, MHS, hunger_rate, meat_yield, leather_yield, move_speed,
                       #   health_scale, is_pen_animal, is_trained_attack, hunt_revenge_chance
    "item":      5,   # quantity, quality(1-7), market_value, hp_pct, is_stuff_material
    "bill":      6,   # target_qty_or_inf, qty_on_hand, min_quality, max_quality, suspended, radius

    # --- meso tier -------------------------------------------------------
    "room":      8,   # wealth, beauty, space, cleanliness, impressiveness, temp_c, is_freezer, tiles
    "bench":     3,   # powered, bill_queue_len, utilization
    "zone":      6,   # fertility, temp_c, growth_days_left, roofed, is_stockpile, is_growing
    "faction":   4,   # goodwill[-100,100], hostility_state, tech_tier, is_pirate

    # --- transient "opportunity" nodes -------------------------------------
    "trader":    4,   # ticks_remaining, channel(0=caravan/1=orbital), beacon_gated, n_items_offered
    "quest":     7,   # difficulty(1-4), accept_deadline, complete_deadline, accepted_flag,
                       #   n_reward_branches, best_branch_value_est, is_endgame
    "caravan":   4,   # n_colonists, n_animals, cargo_weight, cargo_capacity
    "guest":     3,   # is_hostile_risk, hp_pct, is_vip

    # --- macro / world tier ------------------------------------------------
    "research":     7,  # cost_remaining, pct_complete, tier(0-3), n_unlocks,
                         #   + topo_depth, is_reachable_now, hops_to_endgame (from precompute_dag_features)
    "worldmap_tile":5,  # dist_to_home, is_known, terrain_cost, has_ruin, is_settlement
    "event":        5,  # severity, ticks_remaining, is_storyteller_triggered, is_weather, wealth_scaled

    "colony":       6,  # total_wealth, population, day_of_year, avg_mood, difficulty, storyteller_id

    # --- virtual node (synthesized, not extracted from a game entity) ------
    "task_context": 3,  # n_active_research, n_active_bills, n_active_quests -- one instance per graph,
                         #   a master-node shortcut so long Task-DAG chains don't depend on conv depth
}


# =============================================================================
# 2. EDGE TYPES -- (src_node_type, relation, dst_node_type) triples
# =============================================================================

EDGE_TYPES: List[Tuple[str, str, str]] = [
    # spatial / containment (micro <-> meso)
    ("colonist", "occupies",      "room"),
    ("colonist", "has_part",      "bodypart"),
    ("item",     "stored_in",     "room"),
    ("bench",    "located_in",    "room"),
    ("animal",   "housed_in",     "room"),
    ("room",     "adjacent_to",   "room"),
    ("zone",     "adjacent_to",   "zone"),

    # assignment (micro)
    ("colonist", "assigned_to",   "bench"),
    ("colonist", "assigned_to",   "zone"),
    ("colonist", "handles",       "animal"),
    ("colonist", "bonded_to",     "animal"),
    ("colonist", "relates_to",    "colonist"),   # social graph
    ("colonist", "member_of",     "caravan"),
    ("animal",   "member_of",     "caravan"),
    ("item",     "loaded_on",     "caravan"),

    # supply chain / production (micro <-> meso) -- crafting.md, materials.md
    ("bench",    "runs",          "bill"),
    ("bill",     "consumes",      "item"),
    ("bill",     "produces",      "item"),
    ("zone",     "yields",        "item"),
    ("animal",   "produces",      "item"),

    # dependency (macro) -- literally research.md's prerequisite table
    ("research", "prereq_of",     "research"),
    ("research", "unlocks",       "bench"),
    ("research", "unlocks",       "item"),

    # threat / combat (micro <-> world) -- combat.md, events.md
    ("event",    "threatens",     "room"),
    ("event",    "threatens",     "colonist"),
    ("colonist", "targets",       "event"),

    # diplomacy / economy (macro <-> world) -- factions.md, trading.md, questing.md
    ("faction",  "offers",        "quest"),
    ("faction",  "sends",         "trader"),
    ("quest",    "targets",       "room"),
    ("quest",    "targets",       "guest"),
    ("quest",    "targets",       "worldmap_tile"),
    ("quest",    "rewards",       "item"),          # one edge per reward *branch*
    ("quest",    "rewards_goodwill", "faction"),
    ("trader",   "offers",        "item"),          # beacon-range gating applied upstream, at extraction time
    ("caravan",  "located_at",    "worldmap_tile"),
    ("worldmap_tile", "adjacent_to", "worldmap_tile"),
    ("worldmap_tile", "owned_by", "faction"),

    # global context node -- readable from every tier, written by the macro pool
    ("colony",   "context_for",   "colonist"),
    ("colony",   "context_for",   "room"),
    ("colony",   "context_for",   "research"),
    ("colony",   "context_for",   "faction"),

    # virtual master-node shortcut for the Task-DAG -- an O(1)-hop path for
    # long-range chain info, instead of depending on conv depth to bridge a
    # 12-15-hop research chain (see with_reverses for the broadcast direction)
    ("research", "reports_to",    "task_context"),
    ("bill",     "reports_to",    "task_context"),
    ("quest",    "reports_to",    "task_context"),
]


def with_reverses(edge_types: List[Tuple[str, str, str]]) -> List[Tuple[str, str, str]]:
    """Mirror every hetero edge so both endpoints can pass messages.

    Fixed from the first pass: this used to skip same-type edges (src == dst)
    on the assumption they're already symmetric -- true for things like
    room<->room adjacency, but wrong for research-"prereq_of"-research, which
    is directional between *instances* even though both ends share a type.
    Without the reverse, a later tech's value could never inform how urgently
    to prioritize an earlier prereq -- exactly the kind of gap that breaks
    long-chain reasoning. Always adding it costs a redundant (but harmless)
    extra relation for the genuinely-symmetric cases, which is the safe side
    to err on."""
    out = list(edge_types)
    for src, rel, dst in edge_types:
        out.append((dst, f"rev_{rel}", src))
    return out


# =============================================================================
# 2b. PRECOMPUTED DAG FEATURES -- deterministic facts, not learned ones
# =============================================================================
# Reachability and topological depth are exact graph-algorithm facts, not
# uncertain quantities -- computing them once and handing them to the network
# as features frees the GNN's limited effective depth for what's actually
# uncertain (value), rather than spending 3 hops of message passing
# reconstructing what nx.topological_sort already gives exactly. Generic over
# any DAG, so it also applies to bill/item supply chains (cotton -> cloth ->
# apparel), not just research.md's prerequisite table.

def precompute_dag_features(dag: nx.DiGraph, completed: set) -> Dict[str, Dict[str, float]]:
    """
    dag:       a DAG of node ids (e.g. research project ids, or item/bill ids
               chained by consumes/produces edges) with directed edges
               pointing from prerequisite -> dependent.
    completed: ids already finished/unlocked/in-stock.
    Returns per-node: topo_depth (position in a valid topological order),
    is_reachable_now (all its direct prereqs are satisfied), and
    hops_to_endgame (shortest path length to the nearest sink node, i.e. a
    node with no further dependents -- -1 if no sink is reachable from it).
    """
    topo = {n: i for i, n in enumerate(nx.topological_sort(dag))}
    reachable_now = {
        n for n in dag
        if n not in completed and all(p in completed for p in dag.predecessors(n))
    }
    sinks = [n for n in dag if dag.out_degree(n) == 0]

    def hops_to_nearest_sink(n: str) -> int:
        lengths = [
            nx.shortest_path_length(dag, n, s) for s in sinks if nx.has_path(dag, n, s)
        ]
        return min(lengths) if lengths else -1

    return {
        n: {
            "topo_depth": float(topo[n]),
            "is_reachable_now": float(n in reachable_now),
            "hops_to_endgame": float(hops_to_nearest_sink(n)),
        }
        for n in dag
    }


# =============================================================================
# 3. HeteroData CONSTRUCTION
# =============================================================================

def build_hetero_data(state: dict) -> HeteroData:
    """
    Convert one tick's already-extracted game state into a HeteroData object.
    `state[ntype]["x"]` is a pre-shaped [N, dim] tensor per node type;
    `state["edges"][(src, rel, dst)]` is a [2, E] long tensor of indices.
    The actual game-side extractor (outside this file's scope) is responsible
    for producing these from RimWorld's save/live state.
    """
    data = HeteroData()

    for ntype in NODE_FEATURE_DIMS:
        if ntype in state:
            data[ntype].x = state[ntype]["x"]
            data[ntype].id = state[ntype].get("id")   # stable ids, for tracking the same
                                                          # colonist/quest/trader across ticks

    for src, rel, dst in EDGE_TYPES:
        key = (src, rel, dst)
        if key in state.get("edges", {}):
            data[key].edge_index = state["edges"][key]

    return data


# =============================================================================
# 4. THE SHARED "TASK" ABSTRACTION
# =============================================================================
# Bills, Research projects, and Quests are all: (preconditions, cost/investment,
# a completion condition -- often deadline-based, and a reward/unlock set).
# One encoder, reused across all three via a type-conditioning tag, rather
# than three bespoke heads that all reinvent the same shape.

class TaskKind(Enum):
    BILL = auto()
    RESEARCH = auto()
    QUEST = auto()


class TaskEncoder(nn.Module):
    """Shared encoder for anything shaped like (precond, cost, deadline, reward-branches)."""

    def __init__(self, raw_dim: int, hidden: int = 64, n_heads: int = 4):
        super().__init__()
        self.kind_embed = nn.Embedding(len(TaskKind), 8)
        self.body = nn.Sequential(
            nn.Linear(raw_dim + 8, hidden),
            nn.ReLU(),
            nn.Linear(hidden, hidden),
        )
        # variable-length reward branches (0 for a plain bill, up to 3 for a
        # quest) -- attend over them instead of assuming a fixed count
        self.branch_attn = nn.MultiheadAttention(hidden, num_heads=n_heads, batch_first=True)

    def forward(
        self,
        raw_feats: Tensor,             # [N, raw_dim]
        kind_ids: Tensor,               # [N] -- indexes TaskKind
        reward_branch_feats: Tensor,    # [N, max_branches, hidden]
        branch_mask: Tensor,            # [N, max_branches] bool, True where a branch exists
    ) -> Tensor:
        k = self.kind_embed(kind_ids)
        h = self.body(torch.cat([raw_feats, k], dim=-1))     # [N, hidden]
        attended, _ = self.branch_attn(
            h.unsqueeze(1), reward_branch_feats, reward_branch_feats,
            key_padding_mask=~branch_mask,
        )
        return h + attended.squeeze(1)   # residual: task identity + best-attended reward outlook


# =============================================================================
# 5. HIERARCHICAL HETEROGENEOUS ENCODER
# =============================================================================
# micro (colonist/animal/item/bill) -> meso (room/zone/bench/faction)
#   -> macro (colony/research/worldmap/event)

class WeakestLinkPool(nn.Module):
    """Soft version of room-attributes.md's rule: a room's score is weighted
    toward its *worst* input, not a plain average of wealth/beauty/space/
    cleanliness. Used here as the micro -> meso readout for rooms."""

    def __init__(self, dim: int, temperature: float = 4.0):
        super().__init__()
        self.score = nn.Linear(dim, 1)
        self.temperature = temperature

    def forward(self, x: Tensor, batch: Tensor | None = None) -> Tensor:
        s = self.score(x).squeeze(-1)                     # [N]
        w = torch.softmax(-s / self.temperature, dim=0)     # low scorers get more weight
        return (w.unsqueeze(-1) * x).sum(dim=0, keepdim=True)


class TaskDAGEncoder(nn.Module):
    """
    Deeper, separate pass over just the Task-DAG subgraph: research prereqs
    and bill consumes/produces chains. Decoupled from the shallow n_layers=3
    conv over the full colony graph on purpose -- the Task-DAG is small
    (~100-200 nodes) and shaped like a long thin chain, so extra depth here
    is cheap and actually needed, whereas applying it to the whole graph
    would over-squash the wide, shallow parts that don't need it.
    """

    _BASE_EDGES: List[Tuple[str, str, str]] = [
        ("research", "prereq_of", "research"),
        ("bill", "consumes", "item"),
        ("bill", "produces", "item"),
    ]
    TASK_EDGE_TYPES = with_reverses(_BASE_EDGES)

    def __init__(self, hidden: int = 128, n_layers: int = 8):
        super().__init__()
        self.convs = nn.ModuleList([
            HeteroConv(
                {et: GATv2Conv((-1, -1), hidden, add_self_loops=False) for et in self.TASK_EDGE_TYPES},
                aggr="mean",
            )
            for _ in range(n_layers)
        ])

    def forward(
        self, x_dict: Dict[str, Tensor], edge_index_dict: Dict[Tuple[str, str, str], Tensor]
    ) -> Dict[str, Tensor]:
        h = {k: v for k, v in x_dict.items() if k in ("research", "bill", "item")}
        sub_edges = {et: edge_index_dict[et] for et in self.TASK_EDGE_TYPES if et in edge_index_dict}
        for conv in self.convs:
            h = conv(h, sub_edges)
            h = {k: torch.relu(v) for k, v in h.items()}
        return h


class RimWorldEncoder(nn.Module):
    def __init__(self, hidden: int = 128, n_layers: int = 3, task_dag_layers: int = 8):
        super().__init__()
        self.hidden = hidden

        self.input_proj = nn.ModuleDict({
            ntype: nn.Linear(dim, hidden) for ntype, dim in NODE_FEATURE_DIMS.items()
        })

        edge_types = with_reverses(EDGE_TYPES)
        self.convs = nn.ModuleList([
            HeteroConv(
                {et: GATv2Conv((-1, -1), hidden, add_self_loops=False) for et in edge_types},
                aggr="mean",
            )
            for _ in range(n_layers)
        ])

        self.task_dag_encoder = TaskDAGEncoder(hidden, n_layers=task_dag_layers)
        self.task_encoder = TaskEncoder(raw_dim=hidden, hidden=hidden)
        self.room_pool = WeakestLinkPool(hidden)        # micro -> meso, hardcoded inductive bias
        self.colony_pool = nn.Linear(hidden, hidden)     # meso -> macro, fully learned (compare the two empirically)

    def forward(self, data: HeteroData) -> Dict[str, Tensor]:
        x_dict = {
            nt: self.input_proj[nt](data[nt].x)
            for nt in data.node_types if nt in self.input_proj
        }

        for conv in self.convs:
            x_dict = conv(x_dict, data.edge_index_dict)
            x_dict = {k: torch.relu(v) for k, v in x_dict.items()}

        # deep, separate pass over just the Task-DAG (research prereqs + bill
        # consumes/produces chains) -- this is what actually resolves chains
        # longer than n_layers=3 can bridge; overwrite the shallow-conv
        # embeddings for these types with the deep-pass ones before TaskEncoder
        if any(k in x_dict for k in ("research", "bill", "item")):
            x_dict.update(self.task_dag_encoder(x_dict, data.edge_index_dict))

        # route bill/research/quest embeddings through the shared Task encoder
        for ntype, kind in (
            ("bill", TaskKind.BILL), ("research", TaskKind.RESEARCH), ("quest", TaskKind.QUEST)
        ):
            if ntype in x_dict:
                n = x_dict[ntype].size(0)
                kind_ids = torch.full((n,), kind.value - 1, dtype=torch.long)
                # in practice: gather each task's actual reward-branch node embeddings
                # via the "rewards" / "rewards_goodwill" edges; placeholder shape here
                branches = x_dict[ntype].unsqueeze(1)
                mask = torch.ones(n, 1, dtype=torch.bool)
                x_dict[ntype] = self.task_encoder(x_dict[ntype], kind_ids, branches, mask)

        return {
            "node_embeds": x_dict,                                                  # micro-tier policy heads read this
            "room_embed": self.room_pool(x_dict["room"]) if "room" in x_dict else None,   # meso readout
            "colony_embed": self.colony_pool(x_dict["colony"]) if "colony" in x_dict else None,  # macro / manager conditioning
            # after 1+ shallow layers, "reports_to"/"rev_reports_to" have already
            # propagated an O(1)-hop summary here -- the TopManager reads this
            # directly rather than fishing it out of a pooled colony_embed
            "task_context_embed": x_dict.get("task_context"),
        }
