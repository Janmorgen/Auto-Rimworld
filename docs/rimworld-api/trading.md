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

Prices move with the negotiator's Social skill —
[prices & social skill](../rimworld/trading.md#prices--social-skill) — so choosing who talks is
part of the trade, and choosing someone who **can reach the trader** is the other part.

## Naming a want

A shortfall must name **what it needs, not a product**. Asking for `MedicineHerbal` when the
colony's medicine count sums all three tiers means a trader stocking industrial is refused for
having the wrong brand of the right thing. Wants should carry a list of acceptable defs, and
quantities denominated in the unit the shortfall is measured in — nutrition for food, not item
count. **[live]**

## Diagnostics

Four distinct reasons nothing was bought, and they must not share a sentence:

- nothing the colony is short of
- this trader stocks none of what it is short of
- nobody who can reach them and talk
- it is short and the trader has it and it cannot afford it

Collapsing these into one message cost real debugging time. Each names a different thing to fix,
and the third is a reachability problem that looks nothing like the fourth.
