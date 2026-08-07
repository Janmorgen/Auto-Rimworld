# Trading — sessions, tradeables, executing a deal

Mechanics: [trading.md](../rimworld/trading.md) ·
[two ways to trade](../rimworld/trading.md#two-ways-to-trade) ·
[prices & social skill](../rimworld/trading.md#prices--social-skill) ·
[what's tradeable](../rimworld/trading.md#whats-tradeable) ·
[factions.md](../rimworld/factions.md)

## Opening and executing

```csharp
TradeSession.SetupWith(ITrader trader, Pawn negotiator, bool giftMode);
TradeSession.deal.TryExecute(out bool actuallyTraded);
TradeSession.deal.AllTradeables;
```
**[compiles]**

Finding a trader: any spawned pawn whose `TraderKind != null` and whose faction is not hostile.
Caravans and orbital ships are different shapes of the same thing —
[two ways to trade](../rimworld/trading.md#two-ways-to-trade).

## Setting quantities

`Tradeable.CountToTransfer` has an **inaccessible setter**. This is the first thing that stops a
naive implementation, and the probe answers it in a second where the compiler answers it in a
build. **[read]**

Use the force methods instead, then verify **[compiles]**:

```csharp
line.ForceToDestination(count);   // player buys
line.ForceToSource(count);        // player sells
line.ForceTo(count);
line.ActionToDo;                  // confirm it took: TradeAction.PlayerBuys / PlayerSells / None
Transferable.AdjustTo(count);
Transferable.ItemsNeeded;
```

```csharp
enum TradeAction { None = 0, PlayerBuys = 1, PlayerSells = 2 }
```

**Always confirm via `ActionToDo`.** A force call can silently fail to produce the intended
direction, and a trade that reports success while having done nothing is worse than one that
fails loudly.

## Trading is bidirectional in one session

The colony can **sell what it has spare to fund what it needs, inside a single session**. Stocked
on cloth and short of food with no silver is a solvable position, not a refusal. Missing that
scopes the whole capability to "buy if we happen to have money" and throws away most of its value.

**Verified in run 181, and it took twenty runs to see it.** The colony held zero silver, needed
33 days of food against a 25-day winter, and did this: **[live, run 181]**

```
day 7 08h  bought 144 venison, 262 raw fungus, 78 hare meat from Sammy,
           paying with 449 steel — Phoenix negotiating at Social 4, on 0 silver
day 7 09h  bought 11 venison, 20 raw fungus, paying with 36 steel
day 7 15h  bought 11 venison, 20 raw fungus, paying with 36 steel
day 7 16h  bought 4 venison, 8 raw fungus, paying with 15 steel
```

Note the shrinking payments — 449, then 36, then 15. That is the surplus guard draining the free
pool down to its reserve and stopping, not a runaway. Only wood, steel and cloth are ever
offered, and only `held − plan.Needs.For(x) − reserve`, so a wall waiting on steel keeps its
steel. Anything the director tracks no need for is not sold at all, because the failure mode of
guessing there is selling the beds.

Prices move with the negotiator's Social skill —
[prices & social skill](../rimworld/trading.md#prices--social-skill) — so choosing who talks is
part of the trade, and choosing someone who **can reach the trader** is the other part.

## Naming a want

A shortfall must name **what it needs, not a product**. Asking for `MedicineHerbal` when the
colony's medicine count sums all three tiers means a trader stocking industrial is refused for
having the wrong brand of the right thing. Wants should carry a list of acceptable defs, and
quantities denominated in the unit the shortfall is measured in — nutrition for food, not item
count. **[live]**

## What a purchase closed

Buying something is reported. Buying *enough* is not, and the two look identical in the log.

Run 195 measured, day 18–19: **[live, run 195]**

```
day 18 22h  food 2.7d                    (target 19.5d — 15d growing left, then 15d barren)
day 18 23h  bought 284 pemmican, 63 donkey meat, 20 pork, 6 raccoon meat, 2 gazelle meat
            from Camino — Craggy negotiating at Social 1, on 800 silver
day 19 00h  food 5.5d, settling to ~5.0d
day 19 00h  a trader is here and nothing was bought — too dear, 3 silver each against 1 in
            the colony; and could not sell to cover it
```

The colony's entire silver bought about **2.3 settled days against a 16.8-day gap**, and an
hour later it could not afford a single further unit. Both chronicle lines are accurate and the
pair is misleading: the first reads as the shortfall being answered, the second as bad luck with
prices, and nowhere does any number say the answer covered roughly a sixth of the want.

This is the [CapabilityGaps](../../Source/AutoColony/Core/CapabilityGaps.cs) lesson arriving at
trade — *"the fallback itself becoming measurable: bought or found produced nothing across
thirteen days and no number anywhere said so"*. A trade that reports what it bought and what it
paid, but not what it closed, cannot tell a purchase that solved the problem from one that spent
everything on a sixth of it — so every trade reads as a success, and the decision to *keep*
silver for a better-stocked trader can never be made.

The shortfall is already denominated correctly (see **Naming a want** above); what is missing is
the subtraction after the fact, and the comparison against what the same silver would have bought
somewhere else.

## Diagnostics

Four distinct reasons nothing was bought, and they must not share a sentence:

- nothing the colony is short of
- this trader stocks none of what it is short of — **and the message must name what that was**
- nobody who can reach them and talk
- it is short and the trader has it and it cannot afford it

Collapsing these into one message cost real debugging time. Each names a different thing to fix,
and the third is a reachability problem that looks nothing like the fourth.

The second one needed the same treatment a second time. Run 196 spent nine days deadlocked on
wood — walls need it, the map had none within reach, and a trader is the only other source — and
watched two traders arrive and leave. All the record holds is *"this trader stocks none of what
the colony is short of"*, which cannot distinguish a trader who had no wood from a colony that
never asked for any. **[live, run 196]**

Splitting the causes was not enough; a refusal has to name the thing refused. The wants come from
`plan.Needs`, so what the colony can ask for is whatever its goals currently declare — which
makes "was it on the list" a real question with a real answer, and not one the log could reach.
