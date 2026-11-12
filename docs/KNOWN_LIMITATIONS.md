# Known Limitations & Status

Honest status against the master-prompt spec. Updated as work lands.

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

## ❌ Not implemented (spec scope still open)

- **Part install (visual):** `TakeOnCarPartFromSave` is state-only (confirmed: hood did not reappear).
  The runtime visual-mount method is not yet identified (candidates: `SwitchCarPart`, an inventory mount).
- **Part DATA:** condition / variant / tuning / color / dimensions are NOT synced — only the part key.
  Two clients may show the same slot empty/filled but not identical part stats. (Spec §6, §15.)
- **Entity identity:** no `NetworkVehicleId` / `PartInstanceId` / `PartSlotId` yet — we key by car config
  name + bare part name, which only holds for a single car on the lift. (Spec §5.)
- **Concurrency control:** no per-part lock — two players could remove the same part. (Spec §10.)
- **Reconciliation:** no revisions, no canonical hash, no desync detection/repair. (Spec §14.)
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
