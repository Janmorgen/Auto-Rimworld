# agent-notes

Notes on **RimWorld itself**, as established by measurement rather than by reading
documentation or source — the game's `Assembly-CSharp.dll` is not decompilable on this machine,
so anything about how the game behaves internally had to be built and observed.

This is deliberately separate from `HANDOFF.md`, which is about the *director* — its
architecture, its bugs, and the traps in its own code. These notes are about the game the
director is playing, and would still be true if the mod were rewritten from scratch.

| Note | Covers |
|---|---|
| [rimworld-rooms.md](rimworld-rooms.md) | What makes a room, how RimWorld classifies the fifteen room roles, room stats and their bands, mood curves, and the placement API traps found the hard way |
| [food-preservation.md](food-preservation.md) | Why a colony that hunts perfectly can starve indefinitely on a hot map, measured; rot rates, pemmican, and the deadlock the director has no answer to |
| [rimworld-pens.md](rimworld-pens.md) | What makes a pen rather than a room, the forage API and why it must pick the site rather than describe it, and why the lean season is the only number that matters |
| [rimworld-defs-and-chains.md](rimworld-defs-and-chains.md) | Every def the director places with its research gate and cost, the research table, the production chains, and the work-priority shapes |
| [asking-the-right-question.md](asking-the-right-question.md) | Nine faults with one shape — the property that looks like the answer sitting one step nearer than the one that is |
| [rimworld-plants.md](rimworld-plants.md) | What each sowable plant is *for*, why "gives nutrition" is not "is food", and why no recipe walk can ever discover that hops makes beer |
| [mood-and-labour.md](mood-and-labour.md) | Two colonies that died with food and infrastructure at 1.00; why the mood they lose is mostly fixable rather than grief, and why the response added for it cannot fire |

## How these were established

The `showcase` scenario (`AUTOCOLONY_SCENARIO=showcase`) builds one of every room the director
knows about, **states in advance what each is expected to classify as**, and reports what the
game actually calls it. Stating the expectation first is the whole method: an understanding of
rules that cannot be read is only worth something if it is written down before the answer
arrives.

The failures were as useful as the confirmations. Four separate times a wrong measurement
produced a plausible explanation that sent the search in the wrong direction — most expensively
a claim that a bedroom needs an *owned* bed, invented to explain a room that showed as `Room`
when the real cause was that no bed had ever been placed. When a result is surprising, check the
instrument before theorising about the subject.
