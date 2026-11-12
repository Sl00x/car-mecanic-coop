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
- **Mechanical part sync — capture + wire** (ported from CMS21-Together): removing engine/suspension/other
  parts (radiator, timing belts, pulleys, …) fires `PartScript.Hide`, resolves the owning CarLoader, builds
  the positional reference table (`engine=59 driveshaft=1 suspension=84 other=34 body=33`), and broadcasts
  `(type,key,index)`. Verified host→peer: host `[cpart] local UNMOUNT type=0 key=38` → peer
  `<- PART MÉCA … engine key=38`.
- **Mechanical VISUAL apply — CONFIRMED** (FakePeer `ghostmech` → live host): a peer sent 106 mechanical
  unmounts (45 engine + 61 suspension); the host's `ApplyRemote` → `HideBySavegame` ran with **0 errors**
  and the parts **visibly disappeared on screen**. This is the identical code path a rendered client runs
  when it receives a host's action — so client-side apply is validated by proxy. (Two SEPARATE rendered
  clients on one PC is blocked by the game's single-instance guard — see constraints.)
- **Part DATA on mechanical mount — CONFIRMED (capture + wire)**: a MOUNT now carries the part's
  condition/dust/quality/paint(type+rgba)/tuning. Verified host `[cpart] local MOUNT … cond=0.79` → peer
  `MONTÉ … cond=0,79 rgba=(…)`. Read via `PartScript.Condition/Dust/Quality/IsPainted/CurrentPaintType/
  currentColor/tunedID`, applied via `SetCondition/UpdateDust/SetColor/TunePart` (ported from CMS21-Together
  ModPartScript). The on-screen data apply on a 2nd rendered client is unverifiable here (single-instance).
- **Openables (hood / trunk / doors / clamshells) — CONFIRMED (capture + wire + apply)**: opening/closing
  is `CarLoader.SwitchCarPart(carPart, open)` (state = `carPart.Switched`). Captured via a `SwitchCarPart`
  hook, sent on the string channel (`ActSwitch`), applied via `SwitchCarPart(cp, false, switched)`. Verified
  host `[part] local SWITCH 'hood' open=False` → peer `OUVRANT 'hood' FERMÉ`, and host applied a peer's
  switch for hood/trunk/engine_cover/clamshells with 0 errors.
- **Desync detection — now ACCURATE**: the EntityHash used an empty VehicleKey (`@-1`, the #CarLoaderN
  placeholder bug) and ignored mechanical removals. Fixed: `PartSync.RealCar()` resolves the real car
  (`car_vallsen2040@3`), mechanical changes are recorded into the ONE canonical `VehiclePartState`
  (slot `m:type:key:index`), and a re-mount returns the slot to stock (`Unset`) so the hash is a pure
  "set of currently-removed parts". Verified live: a connected peer reports `HASH … => SYNC OK`, and the
  canonical state resets correctly on a car change (observed `removed=[bumper_front,…,m:0:38:-1,…]`).
- **CarLoader resolution fix:** the garage holds several `#CarLoaderN` slots and `GetLastCarLoader()` can
  return an empty placeholder (observed `#CarLoader1`, cache=0). Parts are now resolved from the fired
  `PartScript` via `GetComponentInParent<CarLoader>()` (→ the real `#CarLoader4`, cache=178), with a
  "loader that actually has parts" fallback. This was the whole cause of the earlier empty ref tables.

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
- **Canonical state + revisions + hash (§6/§14):** `VehiclePartState` is the authoritative store in
  `PartSync` (revision bumps per mutation, hash on F9). **Networked desync DETECTION is wired:** the host
  broadcasts an `EntityHash` (vehicle + revision + canonical hash) every ~5 s; a client compares it to its
  local hash and, on mismatch, re-requests the snapshot + catch-up. NOT yet done: *targeted* repair
  (re-sync currently re-pulls the whole snapshot, §14.4) and per-subsystem granularity.
- **Concurrency locks (§10):** `InteractionLockManager` is wired (a remover claims the part; `Tick`
  prunes leases; F9 shows active locks). It is **advisory** — it does not yet BLOCK a conflicting removal,
  because that needs an interaction-START hook (we only have the post-removal hook). Model is unit-tested.
- **F9 debug overlay (§19):** role/peers/RTT/vehicle/revision/removed-count/state-hash/locks + removed
  slot list, from real measurements.

## 🟡 Ported from CMS21-Together (MIT) — built, needs in-game verification

- **Positional part identity** (`CarPartRefs`): engine/suspension/other/driveshaft/body reference tables
  built from the car's structural roots (`e_engine_h`/`ds_h`/`s_*_h`/`Parts`/`carParts`). All interop types
  resolve + compile. Identity = (type, key, index) — stable across machines, replaces fragile strings.
- **Mechanical part sync** (`CarPartSync`): hooks `PartScript.DoMount` (mount) + `PartScript.Hide`
  (unmount), sends `PartState(type,key,index,mounted)`, applies on the peer with the real visual sequence
  (`ShowBySaveGame`/`Show`/`ShowMountAnimation` to mount; `HideBySavegame(false, carLoader)` to unmount).
  **Capture + wire now VERIFIED in-game** (see the verified-live section). Only the on-screen apply on a
  second rendered client remains to confirm (needs two real clients).
- **Body-part mount fix**: `TakeOnCarPartFromSave` + `UpdateCarBodyPart(CarPart)` (the refresh we missed).

## ❌ Not implemented (spec scope still open)

- **Part DATA — custom paint + fluids:** standard paint (type + rgba), condition, dust, quality, tuning are
  now carried on a mechanical mount (see confirmed section). NOT yet: custom-paint `CurrentPaintData`,
  body-part data on the string path, and fluids (`FluidsData.SetLevelAndCondition`). (Spec §6, §15.)
- **Host-validated command flow (§8):** removals are captured post-hoc and broadcast; not yet routed as
  request→validate→commit through the host. True conflict rejection + rollback depend on this.
- **Reconciliation repair (§14.4):** divergence is now *detected accurately* (hash `SYNC OK`/`DESYNC`) and
  a client re-pulls the whole snapshot on mismatch; TARGETED per-part repair is not done.
- **Garage fixtures:** lifts, engine stands, tools/machines not synced. (Spec §16.)
- **Fresh-save trigger:** snapshot uses the last on-disk save; unsaved live changes beyond tracked part
  removals aren't in it. Needs the `SaveGame()` owning type (unknown). (RE map TODO.)

## 🔬 Reverse-engineering hypotheses (unverified)

- `SwitchCarPart(string)` as the runtime install — unconfirmed.
- Inventory `Add/Remove` signatures for part-data capture — not mapped.
- Door/hood/trunk open methods — not located.

## ⛔ Environment constraints (not bugs)

- Installing the mod DLL needs the game CLOSED (DLL locked) → each change is a restart cycle.
- The plugin is split across **two** managed assemblies — `CarMecaSim.Mp.dll` (plugin) and
  `CarMecaSim.Shared.dll` (canonical Coop model: `VehiclePartState`, `InteractionLockManager`, …), which
  IL2CPP loads at runtime. Deploying only `Mp.dll` after changing a Shared type leaves a stale `Shared.dll`
  → `TypeLoadException` on load → game hangs at ~80%. **Always deploy via `tools/Install-Plugin.ps1`**,
  which builds, copies BOTH DLLs, and verifies the referenced types are present in the deployed `Shared.dll`.
- The WinForms launcher (old build) rewrites `mp_config.json` on launch, wiping `LoopbackPort`
  → during loopback tests, launch the game from Steam directly, not via the launcher.
- Testing two *rendered* clients needs two game instances / two Steam accounts — FakePeer validates the
  data/protocol only. **Two game instances on ONE PC is NOT possible**: launching the exe a second time
  (even with `steam_appid.txt` present) exits cleanly (code 0) in ~1 s — the game/Steam single-instance
  guard. `LoopbackNet` gained a client mode (first process binds 48900 = host, second connects = client)
  for this, but it can't be exercised locally for that reason. The visual apply was instead confirmed by
  pointing a peer's mechanical unmounts at the host's own rendered car (`FakePeer ghostmech`).
