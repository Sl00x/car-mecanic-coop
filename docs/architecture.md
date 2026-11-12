# CMS 2021 Co-op — Architecture

Host-authoritative real-time co-op, matching the project schema: **Steam P2P → Host (authoritative
state + validation) → Clients → same car / same state / same world**. The mod coordinates several real
instances of CMS 2021; CMS itself remains the gameplay engine (we never re-simulate mechanics).

## Layers (current code)

```
Plugin.cs ──► MpBehaviour (Unity loop: Update/OnGUI on the MAIN thread)
                 │
   ┌─────────────┼────────────────────────────────────────────────┐
   │             │                                                  │
 Net/ (transport)         Core/ (session)            Sync/ (replication)
 ├ INet (interface)       MpSession                  ├ SaveSync   (full garage via save file)
 ├ SteamNet (P2P)         ├ lobby + peers            ├ CarSync    (garage-changed signal)
 ├ LoopbackNet (TCP test) ├ OnData router            ├ PartSync   (live part removal + late-join)
 └ PacketBuf/Proto        ├ presence/eco/chat        └ OrderSync  (job board)
                          └ SendSaveSnapshot / SendPartAction(To)
 Game/ (interop refs): GameRefs, CarRefs            UI/: Overlay(F6), NativeCoopMenu
```

- **Transport** is abstracted behind `INet` so the real Steam P2P (`SteamNet`) and the headless test
  transport (`LoopbackNet`, 127.0.0.1 TCP) are interchangeable. See `tools/FakePeer` + NETWORK notes.
- **Threading:** all network receive is drained in `INet.Pump()` on the Unity main thread, so
  handlers may touch game objects safely. `LoopbackNet`'s accept/read run on background threads and
  enqueue; `Pump()` dispatches. (The spec's full receive→validate→apply pipeline is partially here.)

## What is authoritative today

- **Garage composition + economy + jobs:** host is the source. A joining client adopts the host's real
  save file (`profile0.cms21b`, ~34 KB) — NOT the empty in-memory serialize. See REVERSE_ENGINEERING_MAP.
- **Live mechanical state (part removals):** host tracks parts removed *this session* (beyond the save)
  and replays them to a late-joiner so their freshly-loaded car matches the current disassembly. Live
  removals replicate both ways via `CarLoader.TakeOffCarPart` capture + apply.

## Replication model (current vs target)

| Concern | Now | Target (spec) |
|---|---|---|
| Initial state | save file + session removal-delta replay | canonical network WorldSnapshot (revisioned) |
| Part removal | capture `TakeOffCarPart`, broadcast key, apply via runtime `TakeOffCarPart` | host-validated command → commit, with part DATA (condition/props) |
| Part install | key only; `TakeOnCarPartFromSave` is state-only (no visual) — runtime mount TBD | full install with exact serialized part data |
| Identity | car config name + bare part key (works for one car on the lift) | `NetworkVehicleId` + `PartInstanceId`/`PartSlotId` (ENTITY_IDENTITY.md) |
| Concurrency | none yet (both can touch same part) | per-resource `InteractionLockManager` |
| Reconciliation | none yet | revisions + canonical hash + targeted resync |
| Openables (door/hood/trunk) | not synced | per-openable state + animation time |

## Anti-replication-loop

A single `PartSync.Applying` flag marks "we are applying a remote action", so the Harmony postfix on
`TakeOffCarPart` does not re-broadcast it. The spec's richer `MutationOrigin` context
(Local/Remote/Snapshot/Reconcile) is the planned generalization.

## Testing without a 2nd Steam account

`LoopbackNet` + `tools/FakePeer` (headless "player 2") exercise the host end-to-end on one machine:
snapshot transfer, presence, chat, economy, latency, and real-time part removal + late-join catch-up.
Offline wire check: `dotnet run --project tools/FakePeer -- selftest`. This cannot test the *visual*
reconstruction on a real second client (FakePeer doesn't render) — that needs two game instances.
