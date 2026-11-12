# Test Plan (§20)

## Automated — offline, no Unity (`dotnet run --project tools/FakePeer -- unittest`)
Pure co-op model. **26/26 passing.** Covers:
- Identity determinism (VehicleKey/PartSlot equal, differ by place/config/slot).
- Canonical state + revisions (remove bumps revision; duplicate remove = no bump / no double-apply).
- Deterministic hash: converged clients share a hash; a client that missed removals diverges; hash is
  order-independent; part data (condition/props) changes the hash (§6 fundamental rule).
- Concurrency locks: acquire free part, block same part / allow different part, owner reporting, lease
  expiry hand-off, release / ReleaseAll, prune expired (§10).
- MutationOrigin scope restore (§11.2).

## Semi-automated — transport, no game (`-- selftest`)
`MockHost` + the real FakePeer client over the loopback framing. **Green.** Validates the wire format +
chunked snapshot reassembly (600000 B / 3 chunks) + Hello/Ping/Chat/Economy/Presence/Latency both ways.

## In-game — host + FakePeer loopback peer (one machine)
Set `LoopbackPort:48900`, launch the game from Steam (not the launcher), enter the garage, then
`FakePeer.exe 48900 <name> <seconds>`.

| Spec test | How here | Status |
|---|---|---|
| A — Spawn / same car | peer receives host save (34 KB) with all cars | ✅ verified |
| C — Removal (A→others) | remove a part → peer receives the key live | ✅ verified |
| G — Late join | peer joins AFTER removals → catch-up replays canonical removed slots | 🟡 code done, needs a post-removal join run |
| Removal apply (peer→host) | peer sends removal → host applies runtime `TakeOffCarPart` (visual) | 🟡 host log confirms apply; on-screen visual pending |
| D — Install (same data) | runtime visual-mount method not found | ❌ not impl |
| F — Conflict (same part) | lock model tested offline; in-game block needs interaction-start hook | ❌ not wired |
| B — Openables | not synced | ❌ not impl |
| H — Reconnect / J — Save / K — Scene | not implemented | ❌ |

## What FakePeer cannot test
Visual reconstruction on a *rendered* second client (FakePeer is headless) and the Steam matchmaking/NAT
layer. Those need two real game instances / two Steam accounts (spec §24 final validation).
