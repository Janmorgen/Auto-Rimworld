"""
rimworld_control_hierarchy.py
==============================
Options-framework (Sutton, Precup & Singh 1999) control hierarchy sitting on
top of RimWorldEncoder's node/tier embeddings (see rimworld_graph_schema.py).

Each Option = (initiation_set, intra-option policy, termination condition),
running for a variable number of primitive ticks before handing control back
up an SMDP level. The Manager (strategic tier) goal-conditions Workers
(tactical/operational tiers) rather than sharing one flat reward signal --
see FeUdal Networks (Vezhnevets et al. 2017) / HIRO (Nachum et al. 2018).

Reward is split by tier for the reasons discussed alongside this file:
dense/shaped for fast tiers (combat), SMDP-return + strategic penalties for
slow tiers (wealth-hoarding vs. raid-size, which only that tier can see).

The Manager itself is now nested (Manager-of-Managers): a single flat
Manager at gamma=0.999 (horizon ~1000 ticks) can't bridge a "research your
way to Spacer tier" span of 5,000-10,000+ ticks -- the payoff has decayed
into noise long before it lands. TopManager operates one level up, along
research.md's own curriculum boundaries (Neolithic -> Medieval -> Industrial
-> Spacer), and hands MidManager a goal instead of MidManager acting alone.
"""

from __future__ import annotations

from abc import ABC, abstractmethod
from dataclasses import dataclass
from enum import Enum
from typing import Optional, Tuple

import torch
from torch import nn, Tensor


# =============================================================================
# 1. OPTION INTERFACE
# =============================================================================

class Option(ABC):
    """One SMDP action: runs several primitive ticks, terminates on its own
    condition rather than after a fixed number of steps."""

    gamma: float   # tier-specific discount -- deliberately NOT a global hyperparameter

    @abstractmethod
    def initiation_set(self, state_embed: dict) -> Tensor:
        """Bool mask over instances (e.g. colonists) this option may start on."""

    @abstractmethod
    def policy(self, state_embed: dict, goal: Optional[Tensor] = None) -> Tensor:
        """Intra-option action distribution, optionally goal-conditioned by a Manager."""

    @abstractmethod
    def terminate(self, state_embed: dict) -> Tensor:
        """Per-instance termination probability beta(s) in [0, 1]."""


# =============================================================================
# 2. REFLEX / TACTICAL TIER -- fast gamma, dense hand-shaped reward
# =============================================================================

class CombatOption(Option, nn.Module):
    gamma = 0.95   # engagement resolves in seconds-to-minutes of game time

    def __init__(self, hidden: int, n_actions: int):
        nn.Module.__init__(self)
        self.net = nn.Sequential(nn.Linear(hidden, hidden), nn.ReLU(), nn.Linear(hidden, n_actions))
        self.term_head = nn.Linear(hidden, 1)

    def initiation_set(self, state_embed: dict) -> Tensor:
        return state_embed["colonist_is_drafted_or_threatened"]   # predicate over colonist nodes

    def policy(self, state_embed: dict, goal: Optional[Tensor] = None) -> Tensor:
        h = state_embed["node_embeds"]["colonist"]
        if goal is not None:
            h = h + goal   # additive goal-conditioning from the Manager
        return torch.softmax(self.net(h), dim=-1)

    def terminate(self, state_embed: dict) -> Tensor:
        h = state_embed["node_embeds"]["colonist"]
        return torch.sigmoid(self.term_head(h)).squeeze(-1)   # -> ~1.0 once no threats remain in range


# =============================================================================
# 3. OPERATIONAL TIER -- medium gamma, terminal + light shaping
# =============================================================================

class QuestExecutionOption(Option, nn.Module):
    """Runs from quest-acceptance to quest-resolution: who to send, when to
    depart, how to route a caravan. Terminates on the quest's own outcome,
    not on a learned probability."""

    gamma = 0.99

    def __init__(self, hidden: int, n_actions: int):
        nn.Module.__init__(self)
        self.net = nn.Sequential(nn.Linear(hidden, hidden), nn.ReLU(), nn.Linear(hidden, n_actions))

    def initiation_set(self, state_embed: dict) -> Tensor:
        return state_embed["quest_accepted"]

    def policy(self, state_embed: dict, goal: Optional[Tensor] = None) -> Tensor:
        h = state_embed["node_embeds"]["quest"]
        if goal is not None:
            h = h + goal
        return torch.softmax(self.net(h), dim=-1)

    def terminate(self, state_embed: dict) -> Tensor:
        return state_embed["quest_resolved"].float()   # success / fail / expiry


class TradeNegotiationOption(Option, nn.Module):
    """Trading isn't a flat categorical action -- it's bundle selection under
    a beacon-visibility constraint and a faction-type price curve. The GNN
    emits per-item marginal utility; bundle choice is handed to a lightweight
    solver rather than forced through a single softmax over all possible
    bundles (which doesn't scale with inventory size anyway)."""

    gamma = 0.98

    def __init__(self, hidden: int):
        nn.Module.__init__(self)
        self.item_utility = nn.Linear(hidden, 1)

    def initiation_set(self, state_embed: dict) -> Tensor:
        return state_embed["trader_present"]

    def policy(self, state_embed: dict, goal: Optional[Tensor] = None) -> Tensor:
        item_h = state_embed["node_embeds"]["item"]
        if goal is not None:
            item_h = item_h + goal
        utilities = self.item_utility(item_h).squeeze(-1)   # [N_items] marginal utility per item
        return utilities   # bundle solver (knapsack-style, outside this module) consumes this

    def terminate(self, state_embed: dict) -> Tensor:
        return state_embed["trader_departed"].float()


# =============================================================================
# 4. TACTICAL-STRATEGIC TIER -- sparse reward, value/planning-driven
# =============================================================================

class QuestAcceptanceOption(Option, nn.Module):
    """
    The accept-timing decision: waiting near a quest's accept-deadline
    preserves optionality without shrinking the completion window, since the
    completion timer only starts on acceptance (questing.md). That value is
    invisible in the current state -- it only shows up under simulation --
    so this head consults a learned dynamics model over a short rollout
    rather than acting off raw current features. This is the model-based /
    planning path earning its keep, not a stylistic choice.
    """

    gamma = 0.999

    def __init__(self, hidden: int, dynamics_model: nn.Module, n_rollout_steps: int = 5):
        nn.Module.__init__(self)
        self.dynamics_model = dynamics_model   # predicts colony_embed(t+k) given a hypothetical action
        self.value_head = nn.Sequential(nn.Linear(hidden, hidden), nn.ReLU(), nn.Linear(hidden, 1))
        self.n_rollout_steps = n_rollout_steps

    def initiation_set(self, state_embed: dict) -> Tensor:
        return state_embed["quest_offered_and_unaccepted"]

    def policy(self, state_embed: dict, goal: Optional[Tensor] = None) -> Tensor:
        colony_embed = state_embed["colony_embed"]
        values = []
        for action in ("accept_now", "wait"):
            future = self.dynamics_model(colony_embed, action=action, steps=self.n_rollout_steps)
            values.append(self.value_head(future))
        return torch.softmax(torch.cat(values, dim=-1), dim=-1)

    def terminate(self, state_embed: dict) -> Tensor:
        return state_embed["quest_deadline_reached_or_decision_made"].float()


# =============================================================================
# 5. NESTED STRATEGIC TIERS -- Manager-of-Managers, never touching primitive
#    actions directly
# =============================================================================
# research.md's own tiers (Neolithic/Medieval/Industrial/Spacer) are already
# an authored curriculum -- use their boundaries as TopManager's stages
# rather than inventing new ones the network has to discover from scratch.

class ResearchTier(Enum):
    NEOLITHIC = 0
    MEDIEVAL = 1
    INDUSTRIAL = 2
    SPACER = 3


class StrategicTier(nn.Module):
    """Shared shape for any manager-of-managers level: read a state
    embedding (+ optionally a goal handed down from above), emit a goal for
    whatever sits below. Each level sets its own gamma/horizon_ticks -- not
    shared across levels, and not shared with any Option either."""

    gamma: float
    horizon_ticks: int

    def __init__(self, hidden: int, goal_dim: int):
        super().__init__()
        self.net = nn.Sequential(nn.Linear(hidden, hidden), nn.ReLU(), nn.Linear(hidden, goal_dim))

    def forward(self, state_embed: Tensor, parent_goal: Optional[Tensor] = None) -> Tensor:
        h = state_embed if parent_goal is None else state_embed + parent_goal
        return self.net(h)


class TopManager(StrategicTier):
    """
    Curriculum-level tier. Emits 'reach Industrial' as a goal, not 'do well
    right now' -- a gamma this long (horizon ~= 1/(1-0.9995) ~= 2000 ticks,
    with horizon_ticks itself set even longer below since goal *refresh* and
    effective *credit-assignment* horizon needn't match 1:1) only makes sense
    because the target is discrete and authored, not something learned from
    scratch. Reads task_context_embed directly -- the O(1)-hop virtual-node
    summary from rimworld_graph_schema.py -- rather than only colony_embed,
    since that's precisely the long-range Task-DAG signal this tier needs.
    """

    gamma = 0.9995
    horizon_ticks = 6000   # spans a full tier transition, not a single task

    def __init__(self, hidden: int, goal_dim: int, n_tiers: int = len(ResearchTier)):
        super().__init__(hidden, goal_dim)
        self.tier_head = nn.Linear(hidden, n_tiers)      # interpretable readout: log this during training
        self.tier_embed = nn.Embedding(n_tiers, goal_dim)

    def forward(
        self, colony_embed: Tensor, task_context_embed: Optional[Tensor] = None
    ) -> Tuple[Tensor, Tensor]:
        h = colony_embed if task_context_embed is None else colony_embed + task_context_embed
        tier_logits = self.tier_head(h)                        # [B, n_tiers]
        target_tier = torch.argmax(tier_logits, dim=-1)
        goal = self.net(h) + self.tier_embed(target_tier)       # continuous goal for MidManager, tier-conditioned
        return goal, tier_logits


class MidManager(StrategicTier):
    """
    The original single-Manager tier -- unchanged gamma/horizon, since that
    was always sized for this level, not for bridging a full research-tier
    transition. Now goal-conditioned by TopManager rather than acting alone.
    Emits the goal Options actually consume; Option.policy(state, goal)'s
    interface doesn't change at all.
    """

    gamma = 0.999
    horizon_ticks = 1000   # re-evaluate roughly this often, not every tick

    def forward(self, colony_embed: Tensor, top_goal: Tensor) -> Tensor:
        return super().forward(colony_embed, parent_goal=top_goal)


@dataclass
class GoalConditionedStep:
    top_goal: Tensor            # from TopManager, refreshed every ~horizon_ticks=6000
    mid_goal: Tensor            # from MidManager, refreshed every ~horizon_ticks=1000, conditioned on top_goal
    active_option: Option        # whichever Option currently holds control
    ticks_since_top_refresh: int = 0
    ticks_since_mid_refresh: int = 0


# =============================================================================
# 6. REWARD SPLIT -- one function per tier, deliberately not sharing a signal
# =============================================================================

def worker_reward(
    delta_hp: Tensor,
    threat_neutralized: Tensor,
    potential_before: Tensor,
    potential_after: Tensor,
    discount: float,
) -> Tensor:
    """Dense, potential-based shaping (Ng, Harada & Russell 1999):
    F(s, s') = gamma * Phi(s') - Phi(s). Guaranteed not to change the optimal
    policy under it, which is what licenses using it at all on a fast tier."""
    shaped = discount * potential_after - potential_before
    return delta_hp + 5.0 * threat_neutralized + shaped


def mid_manager_reward(
    env_reward_over_option: Tensor,
    wealth: Tensor,
    wealth_converted_to_consumables: Tensor,
    hoarding_weight: float = 0.001,
) -> Tensor:
    """SMDP return accumulated over the option's full duration, plus the
    wealth-hoarding penalty. This has to live at MidManager (or higher)
    rather than on CombatOption's reward -- combat has no way to see the
    two-weeks-later raid-size consequence of unspent wealth, so it can't be
    the tier taxed for causing it."""
    hoarding_penalty = hoarding_weight * torch.relu(wealth - wealth_converted_to_consumables)
    return env_reward_over_option - hoarding_penalty


def top_manager_reward(
    tier_progress_before: Tensor,
    tier_progress_after: Tensor,
    tier_advanced: Tensor,
    discount: float,
    tier_advance_bonus: float = 50.0,
) -> Tensor:
    """
    Potential-based shaping at TopManager's own ~6000-tick timescale --
    without this, the only real signal is the terminal tier-transition event,
    which is exactly the kind of multi-thousand-tick gap that makes raw
    real-tick credit assignment hopeless on its own (this is also where the
    dynamics-model/imagined-rollout path from QuestAcceptanceOption earns its
    keep, if you want gradient before the real payoff arrives at all).
    Phi here is tier_progress -- e.g. fraction of the current tier's research
    tree completed, using precompute_dag_features' topo_depth /
    hops_to_endgame as the raw ingredients rather than reinventing a metric.
    """
    shaped = discount * tier_progress_after - tier_progress_before
    return shaped + tier_advance_bonus * tier_advanced
