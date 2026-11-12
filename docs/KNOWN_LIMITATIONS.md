# Known Limitations & Status

Honest status against the master-prompt spec. Updated as work lands.

## ✅ Verified by automated tests (no game)

- **Pure co-op model — 26/26 unit tests** (`FakePeer -- unittest`): deterministic identity
  (VehicleKey/PartSlot), canonical `VehiclePartState` + monotonic revisions, deterministic reconciliation
  hash (convergence + divergence + order-independence + part-data sensitivity), `InteractionLockManager`
  (acquire/conflict/lease-expiry/release/prune), `MutationOrigin` scope. (§5, §6, §10, §11.2, §14.2)
- **Transport/selftest** green (chunked snapshot reassembly + full message exchange).

## ✅ Verified live in-game (host ↔ FakePeer loopback peer)

- Full garage transfer: a joining peer receives the host's real save (`profile0.cms21b`, 34 KB) with all
  cars (`car_vallsen2040` + parts, `car_hinatamoonmk1`, `car_emdenjager`). Byte-exact, chunked.
- Presence/avatars, chat, shared economy (host-authoritative, client deltas applied), job board, latency.
- Real-time part **removal** host→client: removing a part (`hood`, `headlight_left`, `door_front_left`…)
  is captured via `CarLoader.TakeOffCarPart` and received by the peer instantly with its key.
- Client→host **apply**: a peer's removal is applied on the host's car via runtime `TakeOffCarPart`
  (the host log shows `applied remote TAKE-OFF ... (runtime)`).

## 🟡 Implemented, NOT yet confirmed in a live game session

- **Late-join mechanical catch-up:** host tracks this-session removals and replays them to a joiner so
  their car matches the live disassembly. Verified in code + offline selftest (no regression); needs an
  in-game run where a peer joins AFTER the host removed parts.
- **Client→host visual removal:** `TakeOffCarPart` apply builds; the first visual confirmation on the
  host screen is pending (earlier attempt used the state-only `*FromSave` variant — fixed to runtime).
- **Timing:** catch-up removals are sent right after the snapshot; a real client must have the car fully
  loaded (`Ready`) before applying them. No `Ready` gate yet — removals may arrive before the car exists
  and no-op. The spec's spawn state machine (NotPresent→…→Ready) is the fix. FakePeer (headless) doesn't
  expose this.

## 🟡 Modelled + wired, but partial

- **Entity identity (§5):** `VehicleKey`/`PartSlot` built, unit-tested, and `PartSync` keys its canonical
  state by `VehicleKey`. Holds for one car on the lift; garage *move* re-keys (host registry TODO).
- **Canonical state + revisions + hash (§6/§14):** `VehiclePartState` is now the authoritative store in
  `PartSync` (revision bumps per mutation, hash exposed on the F9 overlay). NOT yet wired: networked hash
  *reporting* between peers (EntityHashReport) and automatic targeted *repair*.
- **Concurrency locks (§10):** `InteractionLockManager` is wired (a remover claims the part; `Tick`
  prunes leases; F9 shows active locks). It is **advisory** — it does not yet BLOCK a conflicting removal,
  because that needs an interaction-START hook (we only have the post-removal hook). Model is unit-tested.
- **F9 debug overlay (§19):** role/peers/RTT/vehicle/revision/removed-count/state-hash/locks + removed
  slot list, from real measurements.

## ❌ Not implemented (spec scope still open)

- **Part install (visual):** `TakeOnCarPartFromSave` is state-only (confirmed: hood did not reappear).
  The runtime visual-mount method is not yet identified (candidates: `SwitchCarPart`, an inventory mount).
- **Part DATA capture:** `PartData` (condition/variant/props) is modelled + hashed, but not yet *extracted*
  from the game on removal/install nor carried on the wire — only the part key is sent. (Spec §6, §15.)
- **Host-validated command flow (§8):** removals are captured post-hoc and broadcast; not yet routed as
  request→validate→commit through the host. True conflict rejection + rollback depend on this.
- **Reconciliation repair (§14.4):** divergence is *detectable* (hash) but not auto-repaired over the net.
- **Openables:** doors / hood / trunk open-state not synced. (Spec §9.)
- **Garage fixtures:** lifts, engine stands, tools/machines not synced. (Spec §16.)
- **Fresh-save trigger:** snapshot uses the last on-disk save; unsaved live changes beyond tracked part
  removals aren't in it. Needs the `SaveGame()` owning type (unknown). (RE map TODO.)

## 🔬 Reverse-engineering hypotheses (unverified)

- `SwitchCarPart(string)` as the runtime install — unconfirmed.
- Inventory `Add/Remove` signatures for part-data capture — not mapped.
- Door/hood/trunk open methods — not located.

## ⛔ Environment constraints (not bugs)

- Installing the mod DLL needs the game CLOSED (DLL locked) → each change is a restart cycle.
- The WinForms launcher (old build) rewrites `mp_config.json` on launch, wiping `LoopbackPort`
  → during loopback tests, launch the game from Steam directly, not via the launcher.
- Testing two *rendered* clients needs two game instances / two Steam accounts — FakePeer validates the
  data/protocol only.
