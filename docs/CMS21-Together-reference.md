# CMS21-Together — reference notes (everything we mined from the fork)

Source: **CMS21-Together** by *Fozkais* (MIT), a real-time co-op mod for Car Mechanic Simulator 2021.
These are the techniques we studied and ported into CarMecaSim. Keep this as the map when extending the
multiplayer. Every game API named here was **validated against the real game binary**
(`BepInEx/interop/Assembly-CSharp-firstpass.dll`) with a `System.Reflection.Metadata` probe, so the
signatures are what our build actually exposes (Il2CppInterop regenerates everything `Public`).

---

## 1. Networking model

- **Listen-server, host-authoritative.** The host runs both a server and a local client (loopback to
  127.0.0.1); the host is always client id 1. All canonical state lives on the host (`ServerData`); every
  mutating packet is applied there, then re-broadcast to the other clients.
- **Transports:** Steam relay (`SteamNetworkingSockets`) or Direct IP (TCP reliable + UDP unreliable, port
  7777). Almost everything is sent **reliable**; the only unreliable packet is engine-sound RPM.
- **Join flow:** client connects → handshake → server validates **mod version + game version + owned DLC +
  unique name** → sends a `start` packet carrying only the *gamemode* (NOT a profile) → the client loads into
  a **dedicated scratch save slot** (`MAX_SAVE_COUNT-1`, "NO_SAVE"), so the client's own `profile0.cms21b` is
  never touched → once the garage scene is ready the client **pulls** the world via `resync` requests.
- **Catch-up = client-pull replay**, not a world snapshot: the client asks (`ResyncCar`, `ResyncTools`,
  `ResyncUpgrade`, `ResyncEngineStand`) and the server replays stored state as ordinary packets targeted to
  just that client.

### Our mapping
We are **P2P with the lobby owner as authority** (not a separate server), which is fine for 2 players. We keep
per-subsystem packets like the fork. Our catch-up is host→client on the client's `Ping` (see `MpSession`).

---

## 2. Protocol / serialization

- One `PacketTypes` enum; wire frame = `[int length][int packetId][payload]`.
- Primitives via `BitConverter`; **whole objects via `BinaryFormatter`** (every transported class is
  `[Serializable]`) — a whole car is one `ModNewCarData` graph.
- **Chunking (Steam only):** messages > 384 KB are split into `fragmented` packets keyed by a GUID and
  reassembled on the far side. Direct IP relies on TCP stream reassembly (4 KB buffer).

### Our mapping
We use a compact little-endian `PacketWriter`/`PacketReader` (see `mod/Net/PacketBuf.cs`) instead of
`BinaryFormatter` (BinaryFormatter can't serialize Il2Cpp types anyway). For cars we **don't** mirror the
40-field `ModNewCarData` — we use the game's **native** `NewCarData.Serialize/Deserialize` (see §3). Our Steam
send ceiling guard is `MaxCarBytes = 450KB`; add fork-style chunking if a car ever exceeds it.

---

## 3. Car / garage sync — THE core (what fixed "friend doesn't see the cars")

**Fork model:** a car crosses the wire as `ModNewCarData` (a serializable clone of the game's `NewCarData`
plus `carPosition`/`jobID`). The client reproduces it by configuring the target `CarLoader`
(`placeNo`, `ConfigVersion`, `SetCustomerCar(jobID)`) and calling the vanilla
**`CarLoader.LoadCarFromFile(NewCarData)`** — never a save file. Identity = `carLoaderID` (0-based index from
the `CarLoader_N` GameObject name); `placeNo` is the garage bay.

**Producer hooks (host emits):**
| Hook | Signature | Purpose |
|---|---|---|
| Prefix | `CarLoader.LoadCarFromFile(string)` | car loaded from file → publish car |
| Postfix | `CarLoader.LoadCar(string)` | job/customer car loaded → publish |
| Prefix | `CarLoader.DeleteCar()` | car removed → delete packet |
| Prefix | `CarLoader.ChangePosition(int)` | garage bay move |
| Postfix | `CarLoader.TakeOffCarPart(string,bool)` | body part off → `ModCarPart` |
| Postfix | `PartScript.DoMount()` / `PartScript.Hide()` | mechanical part on/off → `ModPartScript` |
| Postfix | `FluidsData.SetLevel(...)` | fluid change |

**Readiness gating (critical — IL2CPP car loads are async coroutines):**
- Global `GameLoadHook.isGameReady` (set true after a ~2s settle); nearly every consumer spins
  `while (!IsGameReady()) yield WaitForSeconds(0.25f)`.
- Per-car: wait `carLoaders[id].IsCarLoaded()`, then extra `WaitForEndOfFrame`s.
- Per-car part refs: `PartsReferencer.GetPartReferences` builds 5 reference dicts then sets `car.isReady`;
  part-delta consumers block on `isReady`.
- **Anti-echo:** each consumer sets the matching producer's `listen=false` right before calling the vanilla
  method, so applying a remote change doesn't re-emit a packet.

### Our mapping (validated against the binary — see `mod/Game/CarCodec.cs`, `mod/Sync/CarSync.cs`)
- `NewCarData` is a **struct** with native `Serialize(BinaryWriter, byte)` / `Deserialize(BinaryReader, byte)`;
  version byte = **`GlobalData.CurrentSaveVersion`** (static Byte; `Constants.SaveVersion=5`). So we serialize
  ONE car to bytes with the game's own writer — no `ModNewCarData` mirror needed.
- Client respawns with **`CarLoader.LoadCarFromFile(NewCarData)`** (the private IEnumerator overload, exposed
  Public by interop). `placeNo`/`ConfigVersion`/`orderConnection` are **get-only** on our build (no setters) —
  so we DON'T set them; we pick the loader whose `GetPlaceNo()==place` (the bay is fixed) and read
  `cd.customerCar`/`cd.orderConnection` to call `SetCustomerCar(true, jobId)`.
- **No whole-profile overwrite, no `LoadInstant`** — that fragile path (deserialize the host's whole
  `profile0.cms21b` + `LoadInstant(true)` + `LoadCarFromFile(place)` reading the *client's* save) is what threw
  **"assertion failed"** on a guest. Gone for cars.
- Wire: `PacketType.CarData` = {`OpSpawn(place,bytes)`, `OpDelete(place)`, `OpClear`}. Host capture is deferred
  to `CarSync.Tick` (the load is a coroutine): mark the place dirty → wait `IsCarLoaded()` → `SaveCarToFile()`
  → read `carsInGarage[place]` → serialize → broadcast. Client queues ops and applies when `InGarage`. Join
  catch-up = `OpClear` then one `OpSpawn` per occupied loader.
- Live part/paint/openable edits still ride our existing `PartState`/`PartAction` packets, so a car spawned in
  its SAVED state converges to the host's live disassembly.
- **Deferred** (only rode the removed snapshot): warehouse inventory + garage upgrades. If host/client garage
  LEVELS differ, places without a matching client loader are skipped (graceful, no crash).

---

## 4. Part-level data model (for reference / future inventory sync)

- `ModNewCarData` ↔ `NewCarData`: `carToLoad`, `configVersion`, `customerCar`, `orderConnection`, `color`/
  `factoryColor` (float[4]), `PaintData`, `BodyPartsData`, `PartData`, `EngineData`, `FluidsData`, wheels
  (`rimsSize`/`tiresSize`/`tiresET`/`wheelsWidth`), tuning (`ecuData`), `LicensePlatesData`, etc.
- `ModCarPart` ↔ body `CarPart`: `switched`, `condition`, `unmounted`, `tunedID`, tint/paint, `Dust`,
  `washFactor`, recursive `connectedParts`.
- `ModPartScript` ↔ mechanical `PartScript`: `id`, `tunedID`, `isExamined`, `isPainted`, `paintData`,
  `color`, `quality`, `condition`, `dust`, `unmounted`, routing keys `partID`/`partIdNumber`/`type`
  (`ModPartType`: engine/suspension/other/driveshaft/body).

---

## 5. Avatars / animations / assets

- The fork ships **`player.assets`** — a 14 MB Unity AssetBundle with a skinned mechanic (`playerModel`),
  textures `tex_base`/`tex_normal`, and a **baked Animator** = a 2D locomotion blend tree driven by two floats
  **`Vertical`/`Horizontal`**. We ship this bundle (MIT) next to our DLL so a guest sees an animated body.
- Animation is **reconstructed locally from position deltas** — NOT sent. Remote avatars receive only
  position + rotation (per-frame, >0.01 delta, reliable). "Working on a car" is just **hiding the avatar**
  (`SetActive(false)`), there is **no working/tool animation** and no tool-in-hand.
- Load pattern (IL2CPP-safe): `AssetBundle.LoadFromFile` → `LoadAsset(name, Il2CppType.Of<GameObject>()).Cast<GameObject>()`
  → rebuild the material with the game's **live HDRP shader** + the bundle textures (the baked shader won't
  resolve across Unity versions) → drive `Animator.SetFloat("Vertical"/"Horizontal", Lerp(...))`.

### Our mapping (`mod/Core/BundleAvatar.cs`, `RemotePlayer.cs`)
Avatar preference order: **our own `mechanic.cmch`** (real CMS2026/2027 mechanic, custom no-Unity-license
format) → **`player.assets`** (fork, animated) → primitives. Host keeps `mechanic.cmch`; the guest
distribution ships `player.assets` so the guest sees an animated body instead of capsules.

---

## 6. Game-cursor / input (menu usability, not from the fork but same game)

At `timeScale=0` the game's `Update()` still runs, so the FPS controller keeps grabbing the mouse and gameplay
keybinds still fire while you type. Fix: set **`FPSCamera.readInput = false`** while a mod panel is open (the
game toggles this itself during its own pause), and re-assert `Cursor.visible/lockState` in **OnGUI** (runs
after the game's Update). The game's cursor is a **`Cursor3D`** component (`ShowCursor`, `SetUICursor`,
`BlockCursor`, `SetCursorEnabled`). Cheats live on `CarDebug` (`RepairAll`/`RefillFluids`/`SetDust`),
`CarLoader.SetConditionAll(float)`, `UpgradeSystem.UnlockAll(bool)`, `TestScript.*` (debug MonoBehaviours —
may be absent in release, so best-effort).

---

## 7. Open follow-ups (to make MP fully "work correctly")

1. **Validate the data-driven car spawn live** with a 2nd player (binary-validated, not yet confirmed in-game).
2. **Inventory + garage-upgrade sync** (deferred from the snapshot removal) — port the fork's `requestAddItem`/
   `garageUpgrade`/`resync` ideas as their own packets.
3. **Mid-session garage-car loads** via `LoadCarFromFile` (not just job cars) — add a host hook if needed.
4. **Tool/fluid live sync** (lifter, spring clamp, tire changer, welder, paint, engine crane) — the fork has a
   packet per tool; we have openables + parts but not tools yet.
5. Avatar **tool-in-hand / working pose** — neither the fork nor we do this; would need bone attach + a state.
