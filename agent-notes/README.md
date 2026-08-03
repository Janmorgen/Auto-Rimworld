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
