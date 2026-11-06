# Car Mechanic Simulator 2021 — Game State, Save/Load, Economy

Reverse-engineering notes for a co-op multiplayer mod. Source: `re/output/dump.cs`
(Il2CppDumper, Unity 2020.3.49f1, IL2CPP, x86-64). All RVA/Offset values are from that dump.

Key takeaway up front: **`GameManager` is a container of sub-managers and does NOT hold money. The live
economy (money / scraps / XP / level / jobs) lives in the static class `GameSettings`. The persisted game
state is the `ProfileData` class, serialized to a custom binary blob via `BinaryWriter`/`BinaryReader`.**

---

## 1. The `GameManager` Singleton

- Declaration: `public class GameManager : Singleton<GameManager>` (TypeDefIndex 9084, dump line 448947).
- Base: `public class Singleton<T> : MonoBehaviour` (TypeDefIndex 8810, line 441296).

### Singleton access pattern

`Singleton<T>` exposes a static property `Instance`:

```
private static T _instance;        // 0x0
private static object _lock;       // 0x0
private static bool applicationIsQuitting; // 0x0
public static T Instance { get; }  // -> get_Instance()
```

Access the instance with **`GameManager.Instance`** (compiler emits `Singleton<GameManager>.get_Instance`).
The concrete generic instantiation is:

```
Singleton<GameManager>.get_Instance  // RVA: 0x14ED8C0  Offset: 0x14EBEC0  VA: 0x1814ED8C0
```

So in a native hook / Frida you call the generic-shared `get_Instance` at RVA `0x14ED8C0`.
`GameManager.Awake()` (RVA 0x1038890) assigns the instance and wires up all sub-managers.

### GameManager fields (all are references to sub-systems; money is NOT here)

| Field | Type | Offset | Meaning |
|---|---|---|---|
| `Inventory` | `Inventory` | 0x18 | Live player parts inventory (public) |
| `Warehouse` | `Warehouse` | 0x20 | Live warehouse storage (public) |
| `_tempInventory` | `TempInventory` | 0x28 | Transient inventory (prop `TempInventory`) |
| `_carBundleLoader` | `CarBundleLoader` | 0x30 | Car asset bundle loader |
| `bonusPartsManager` | `BonusPartsManager` | 0x38 | Bonus parts |
| `_RDGPlayerPrefs` | `RDGPlayerPrefs` | 0x40 | Wrapper over PlayerPrefs (key/value) |
| `_localization` | `Localization` | 0x48 | Localization |
| `_carBundleManager` | `CarBundleManager` | 0x50 | Car bundles |
| `_rustmapManager` | `RustmapManager` | 0x58 | Rust maps |
| `liveriesManager` | `LiveriesManager` | 0x60 | Liveries |
| `_difficultyManager` | `DifficultyManager` | 0x68 | **Difficulty (game mode)** |
| `_FPSInputController` | `FPSInputController` | 0x70 | Input |
| `_inputManager` | `InputManager` | 0x78 | Input |
| `_partScriptCullerManager` | `PartScriptCullerManager` | 0x80 | Rendering cull |
| `_profileManager` | `ProfileManager` | 0x88 | **Profile selection / save orchestration** |
| `_upgradeSystem` | `UpgradeSystem` | 0x90 | **Workshop upgrades** |
| `_gameDataManager` | `GameDataManager` | 0x98 | **Holds `ProfileData[]` + save/load** |
| `_platformManager` | `PlatformManager` | 0xA0 | **Platform save/achievements abstraction** |
| `_engineAudioController` | `EngineAudioController` | 0xA8 | Audio |
| `_engineStartAudio` | `EngineStartAudio` | 0xB0 | Audio |
| `OrderGenerator` | `OrderGenerator` | 0xB8 | **Jobs/orders generator (public)** |
| `CarLoadersInScene` | `List<CarLoader>` | 0xC0 | **Cars currently loaded in scene (public)** |
| `Radio` | `Radio` | 0xC8 | Jukebox/radio (public) |
| `_xboxConnectedStorageManager` | `XboxConnectedStorageManager` | 0xD0 | Xbox storage |
| `screenshotsManager` | `ScreenshotsManager` | 0xD8 | Screenshots |
| `cacheManager` | `CacheManager` | 0xE0 | Cache |
| `eventManager` | `EventManager` | 0xE8 | Event bus |

Every private field has a public property accessor (e.g. `get_DifficultyManager` RVA 0x1039450,
`get_GameDataManager` RVA 0x1039DF0, `get_ProfileManager` RVA 0x1039A30, `get_PlatformManager` RVA 0x1039F30).

Other relevant singletons (not under `GameManager`):
- `GameScript.Get()` (RVA 0x3366... static, line 431891) — in-scene gameplay controller, holds `SceneType CurrentSceneType` (0x18). This is where **SellCar / BuyCar / EndJob** live.
- `GameMode.Get()` (RVA 0x103E580) — the mechanic *interaction* mode (part-select / mount / unmount), **not** difficulty. Field `gameMode currentMode` (0x30).
- `GameInventory.Instance` (RVA 0x102CA20) — static parts/tuning **database** (read-only reference data loaded from TextAssets), not per-player mutable state.

---

## 2. The Save-Data Model

### `ProfileData` — the full persisted game state (TypeDefIndex 8947, line 445587)

One instance per save slot. This is the authoritative snapshot of a saved game.

| Field | Type | Offset | Meaning |
|---|---|---|---|
| `saveVersion` | `byte` | 0x10 | Save format version (current = 5) |
| `Name` | `string` | 0x18 | Profile name |
| `LastSave` | `long` | 0x20 | Last save timestamp |
| `PlayTime` | `uint` | 0x28 | Seconds played |
| `BestRaceTime` | `long` | 0x30 | Drag best time |
| `TopSpeed` | `int` | 0x38 | Record top speed |
| `LastUID` | `long` | 0x40 | Last allocated item UID (see `UIDManager`) |
| `BuildVersion` | `string` | 0x48 | Game build that wrote the save |
| `FinishedTutorial` | `bool` | 0x50 | Tutorial done |
| `Difficulty` | `DifficultyLevel` | 0x54 | **Difficulty / game mode of this save** |
| `carsInGarage` | `NewCarData[]` | 0x58 | **Cars in garage (full per-car state)** |
| `carsOnParking` | `NewCarData[]` | 0x60 | **Cars on parking lot** |
| `jukeboxData` | `RadioData` | 0x68 | Radio state |
| `garageCustomizationData` | `GarageCustomizationData` | 0x70 | Garage cosmetics |
| `inventoryData` | `NewInventoryData` | 0x80 | **Serialized inventory** |
| `warehouseData` | `NewWarehouseData` | 0x88 | **Serialized warehouse(s)** |
| `carLiftersData` | `NewCarLifterData[]` | 0x90 | Lift state |
| `carLoaderData` | `NewCarLoaderData` | 0x98 | Active car loader state |
| `unlockedPosition` | `NewUnlockedPosition` | 0xA0 | Unlocked workshop positions |
| `upgradeForPointsData` | `NewUpgradeSystemData` | 0xA8 | Skill-point upgrades |
| `upgradeForMoneyData` | `NewUpgradeSystemData` | 0xC0 | Money upgrades |
| `machines` | `NewMachines` | 0xD8 | Owned machines/tools |
| `jobsData` | `NewJobsData` | 0xE0 | **Current orders/jobs** |
| `globalDataWrapper` | `NewGlobalDataWrapper` | 0xE8 | **Money / level / XP / scraps (see §3)** |
| `PlayerData` | `PlayerData` | 0xF0 | Player world position & rotation |
| `ShopListItemsData` | `ShopListItemData[]` | 0x100 | Shopping list |
| `PaintshopData` | `PaintshopData` | 0x108 | Paint shop state |
| `WindowTintData` | `WindowTintData` | 0x118 | Window tint colours |

Key methods (ProfileData):
- `byte[] SerializeToBytes()` — RVA 0x987D30 / Offset 0x986330. **Produces the full save blob.**
- `void DeserializeFromBytes(ref byte[] bytes)` — RVA 0x988750 / Offset 0x986D50. **Rebuilds state from a blob.**
- `void WriteSaveHeader(BinaryWriter)` / `bool ReadAndVerifyHeader(BinaryReader)` — magic header.
- `void WriteSaveVersion(BinaryWriter)` / `bool ReadAndVerifySaveVersion(BinaryReader)` — version gate.
- `void Init()`, plus per-section `Init*` and `Recover*` helpers; `TryRecoverProfile()` for backup recovery.

### `NewGlobalDataWrapper` — persisted economy/progression (TypeDefIndex 8746, line 437593)

This is the on-disk copy of the global scalars (the live copy is in `GameSettings`, §3).

| Field | Type | Offset | Meaning |
|---|---|---|---|
| `PlayerMoney` | `int` | 0x10 | **Saved money** |
| `PlayerLevel` | `int` | 0x14 | Saved level |
| `PlayerExp` | `int` | 0x18 | Saved XP |
| `PlayerScraps` | `int` | 0x1C | Saved scrap currency |
| `BarnsAmount` | `int` | 0x20 | Barns owned |
| `MissionsFinished` | `int` | 0x24 | Missions done |
| `CurrentMissionDone` | `bool` | 0x28 | Current mission done |
| `UnlockedParkingLevels` | `int` | 0x2C | Parking floors unlocked |
| `StoryMissionInProgress` | `bool` | 0x30 | Story mission flag |

Methods: `Serialize(BinaryWriter)` RVA 0x9D3C40, `Deserialize(BinaryReader)` RVA 0x9D3D50.

### `ProfileData` summary record (`ProfileManager`-level) — `PlayerData` (TypeDefIndex 8723, line 436960)

`struct PlayerData { FloatArrayWrapper SavedPosition (0x0); FloatArrayWrapper SavedRotation (0x8); }`
with `GetPosition/SetPosition/GetRotation/SetRotation` and `Serialize/Deserialize`.

Virtually every nested save type (`NewCarData`, `RadioData`, `ToolsData`, `GarageCustomizationData`,
`WindowTintData`, `WarehouseNameData`, …) implements its own `Serialize(BinaryWriter)` /
`Deserialize(BinaryReader)` — see §4.

---

## 3. Economy — where money lives and how it changes

### Live runtime economy: static class `GameSettings` (TypeDefIndex 8618, line 432129)

All of these are **static** (process-global, single copy). Offsets are into the static field block.

| Field | Type | Offset | Meaning |
|---|---|---|---|
| `PlayerMoney` | `int` | 0x2A0 | **Live money (the value shown in HUD)** |
| `PrevPlayerMoney` | `int` | 0x2A4 | Previous money (for animated delta) |
| `AddMoneyAmount` | `int` | 0x2A8 | Pending add amount |
| `PlayerScraps` | `int` | 0x2AC | Live scraps |
| `PrevPlayerScraps` | `int` | 0x2B0 | |
| `AddPlayerScrapsAmount` | `int` | 0x2B4 | |
| `PlayerExp` | `int` | 0x2B8 | Live XP |
| `PlayerLevel` | `int` | 0x2C8 | Live level |
| `Jobs` | `int` | 0x2D4 | Active job count |
| `MissionsFinished` | `int` | 0x2E0 | |
| `MissionsAmount` | `int` | 0x2E8 | |
| `UnlockedParkingLevels` | `int` | 0x1E8 | |
| `UnlimitedMoney` | `bool` | 0xD0 | Cheat/difficulty flag |
| `UnlimitedScraps` | `bool` | 0xD1 | |
| `CurrentSaveVersion` | `const byte` | — | = 5 |
| `EasyModeMoney` | `const int` | — | = 10000 |
| `SandboxModeMoney` | `const int` | — | = 900000000 |

Money-mutating methods (all static) — **these are the primary hook points**:

| Method | Signature | RVA / Offset |
|---|---|---|
| Add money | `static void AddPlayerMoney(int money)` | RVA 0xD7B580 / 0xD79B80 |
| Set money | `static void SetPlayerMoney(int money)` | RVA 0xD7B850 / 0xD79E50 |
| Starting money | `static int GetStartingMoney(DifficultyLevel)` | RVA 0xD7B910 / 0xD79F10 |
| Add scraps | `static void AddPlayerScraps(int amount)` | RVA 0xD7B930 / 0xD79F30 |
| Set scraps | `static void SetPlayerScraps(int scraps)` | RVA 0xD7BBA0 / 0xD7A1A0 |
| Add XP | `static void AddPlayerExp(int exp, bool instant = false)` | RVA 0xD7BCC0 / 0xD7A2C0 |
| Add job | `static void AddJob(int amount)` | RVA 0xD7C430 / 0xD7AA30 |
| Settings save | `static bool Save()` | RVA 0xD7C990 |
| Settings load | `static void Load()` | RVA 0xD7CB90 |

Note: `GameSettings.Save()/Load()` save **client settings** (video/audio/input), not the profile/money.

### High-level economy transactions: `GameScript` (TypeDefIndex 8617, line 431775)

These call into `GameSettings.AddPlayerMoney` under the hood and are the gameplay-level entry points:

- `void SellCar(CarLoader carLoader, int sellPrice)` — RVA 0xE95330
- `void BuyCar(CarLoader carLoader, int buyPrice)` — RVA 0xE95560
- `void EndJob(Job job, CarLoader carLoader)` — RVA 0xE95830 (pays the player for a completed order)
- plus coroutines `SellCarCoroutine` (0xE95450), `EndJobCoroutine` (0xE959A0).

### Per-profile money queries: `ProfileManager` (TypeDefIndex 8703, line 436054)

- `int GetProfileMoney(int index)` — RVA 0x98A1A0
- `void UpdateStartingMoney(DifficultyLevel difficultyLevel)` — RVA 0x98B2F0 (seeds money when a new profile is created)
- Also `GetSelectedProfileData()`, `GetProfileLevel()`, `GetSelectedProfileDifficulty()`.

Relationship: **`GameSettings.PlayerMoney` (live)** is the source of truth during play;
on save it is copied into **`ProfileData.globalDataWrapper.PlayerMoney`**; on load the reverse happens.

---

## 4. Save/Load Mechanism & Serialization Format

### Serialization format: **custom binary** (`System.IO.BinaryWriter` / `BinaryReader`)

The game save is NOT JSON and NOT `FullSerializer`. Every save type defines
`Serialize(BinaryWriter)` / `Deserialize(BinaryReader)`. `ProfileData.SerializeToBytes()` opens a
`MemoryStream`+`BinaryWriter`, writes a header (`WriteSaveHeader`), a version byte
(`WriteSaveVersion`, version 5), then each sub-structure in turn, and returns the `byte[]`.
`DeserializeFromBytes(ref byte[])` verifies header + version and reads it back.
(`FullSerializer - Unity.dll` in the assembly list is used for config/JSON elsewhere — e.g. mod/livery
metadata and settings — not for the main profile save.)

### Save orchestration: `GameDataManager` (TypeDefIndex 9083, line 448865)

Held at `GameManager.Instance.GameDataManager`.

| Member | Type / Signature | Offset / RVA |
|---|---|---|
| `SaveIsLoaded` | `bool` | 0x10 |
| `ProfileData` | `ProfileData[]` | 0x18 (all slots) |
| `ProfilesUpdated` | `bool` | 0x20 |
| `CurrentProfileData` | `ProfileData` (property `get_CurrentProfileData`) | RVA 0x10297E0 |
| Save slot | `void Save(int profileID)` | RVA 0x102BB00 |
| Load selected | `void LoadProfile()` | RVA 0x102C180 |
| Load (coroutine) | `IEnumerator Load(bool forceLoad = false)` | RVA 0x102AB10 |
| Load (sync) | `void LoadInstant(bool forceLoad = false)` | RVA 0x102ABD0 |
| Rebuild from disk | `void ReloadProfiles(SaveData[] saveData)` | RVA 0x102BE10 |
| Per-car save | `void SaveCarInGarage/SaveCarInParking/SaveCar(...)` | — |
| DLC check | `int CheckSaveHaveUsedDLCs(int profileIndex)` | RVA 0x102C5D0 |

### Platform abstraction (actual disk/cloud I/O)

`GameManager.Instance.PlatformManager` → `Platform` (abstract, CMS.Platforms, TypeDefIndex 10447, line 507996),
which exposes `public BaseSave SaveSystem;` (0x38) and `GetPlatformSave()` (RVA 0x…508230).

`BaseSave` (abstract MonoBehaviour, TypeDefIndex 10504, line 510032):

| Member | Signature | RVA |
|---|---|---|
| `SaveData` | `SaveData[] SaveData` (0x18) | — |
| `IsSaveInProgress` | `bool` (prop) | — |
| `GetSave` | `abstract SaveData[] GetSave()` | — |
| `GetSaveCoroutine` | `virtual IEnumerator GetSaveCoroutine()` | 0xB5C150 |
| `SendSave<T>` | `abstract IEnumerator SendSave<T>(string path, T data)` | — |
| `DeleteSave` | `abstract void DeleteSave(string fileName)` | — |

Concrete PC implementation `SteamSave : BaseSave` (TypeDefIndex 10475, line 508916):
- `override SaveData[] GetSave()` — RVA 0xD9CEF0
- `private byte[] LoadProfileSave(int profileIndex, out byte format, out bool hasData)` — RVA 0xD9D200 (reads a slot's bytes from disk)
- `override IEnumerator SendSave<T>(string fileName, T data)` — writes bytes to disk
- `override void DeleteSave(string fileName)` — RVA 0xD9D3B0

`SaveData` struct (TypeDefIndex 10333, line 504725) — the transport container:
```
struct SaveData { bool HasData (0x0); byte[] Data (0x8); byte Format (0x10); }
```
`Data` holds the `ProfileData.SerializeToBytes()` blob; `Format` tags the platform encoding.

### File locations (`GlobalStrings`, TypeDefIndex 8625, line 432655)

- `SaveDirectory` (static readonly string, built from `Application.persistentDataPath`).
- `SaveExtension = ".cms21"`, PC `".cms"`, Xbox `".xboxcms"`, PS4/PS5 `".ps4cms"/".ps5cms"`.
- `ProfileManager.BackupSave()` (RVA 0x98B8A0) and `BackupNewSave(persistentDataPath, fileName, backupDirectory)`
  (RVA 0x98BB10) maintain rolling backups; `ProfileData.TryRecoverProfile()` restores from them.

### Load-completed event

`GameSaveLoadedArgs : EventArgs` (TypeDefIndex 10815, line 524800) carries `byte[] Data` (property `Data`),
raised through `Gdk.OnGameSaveLoadedHandler` — a clean signal that a save blob has been loaded.

### NOT the game save: UnityEngine.UI.Extensions sample

`SaveGame` (TypeDefIndex 9400), `SaveLoad` (static, `Save(SaveGame)`/`Load(string)` with `SurrogateSelector`
+ `ColorSurrogate`/`Vector3Surrogate`/… `ISerializationSurrogate`), and `SaveLoadMenu` are the **UI Extensions
demo** scene-object serializer (BinaryFormatter-based). Ignore them for the real profile pipeline.

### Secondary save paths

- `Inventory.Save()` (RVA 0x91EC60) / `Inventory.Load()` (0x91E3C0) — serialize the live inventory into `inventoryData`.
- `Warehouse.Save()` (0x91EC60-area) / `Warehouse.Load()` (0x91E3C0-area).
- `UpgradeSystem.Save()` (0xB36240) / `Load()` (0xB35B50) / `LoadSave()` (0xB36A90).
These feed the corresponding `ProfileData` sub-records.

---

## 5. Co-op Sync Notes

### Host-authoritative global state that MUST be replicated to clients

All of this is single-copy global / per-profile state; in co-op only the host should own it and push
deltas (or snapshots) to clients:

1. **Economy & progression** — `GameSettings.PlayerMoney/PlayerScraps/PlayerExp/PlayerLevel/Jobs`
   (static, offsets 0x2A0/0x2AC/0x2B8/0x2C8/0x2D4). These are process-global; clients must mirror the
   host's values, never mutate locally.
2. **Inventory** — `GameManager.Instance.Inventory.items` (`List<Item>`, 0x18) and `groups` (0x20).
   Part ownership/condition is shared loot.
3. **Warehouse** — `Warehouse.warehouseList` (`List<List<Item>>`, 0x18), `warehouseNamesData`,
   `Warehouse.amountOfUnlockedWarehouses` (static).
4. **Orders / Jobs** — `OrderGenerator.jobs` / `selectedJobs` (0x28 / 0x30). Must be generated by the host
   and broadcast; otherwise each client generates divergent random jobs.
5. **Cars in scene & their full state** — `GameManager.Instance.CarLoadersInScene` (0xC0) plus the
   per-car `NewCarData` (mounted parts, fluids, paint, ECU, alignment…). Largest/most complex payload.
6. **Upgrades** — `UpgradeSystem.UpgradesForPoints/UpgradesForMoney/availablePoints`,
   and `UnlockedParkingLevels`, `unlockedPosition`.
7. **Difficulty** — `DifficultyManager.currentDifficulty` (0x18). Session-wide; set once, must match for all
   peers (controls money/XP multipliers, unlimited-money flags, etc.).
8. **Scene / location** — `GameScript.CurrentSceneType` (0x18) and `GameMode.currentMode` for interaction gating.

Client-local (do NOT globally sync): `GameInventory` (static parts DB — identical on all peers, load locally),
`GameSettings` video/audio/input settings, `RDGPlayerPrefs`, UI state, camera.

### Recommended hook points

**Full-state snapshot on client join (cleanest bootstrap):**
- Host: `GameManager.Instance.GameDataManager.CurrentProfileData` → `ProfileData.SerializeToBytes()`
  (RVA 0x987D30). Ship the resulting `byte[]` to the joining client.
- Client: `ProfileData.DeserializeFromBytes(ref byte[])` (RVA 0x988750) to reconstruct the world,
  then reload scene objects. This reuses the game's own save format as the network snapshot format.

**Live delta hooks (intercept state mutations on the host, broadcast to clients):**

| Event to replicate | Hook (method · RVA) |
|---|---|
| Money changed | `GameSettings.AddPlayerMoney` (0xD7B580), `GameSettings.SetPlayerMoney` (0xD7B850) |
| Scraps changed | `GameSettings.AddPlayerScraps` (0xD7B930), `SetPlayerScraps` (0xD7BBA0) |
| XP / level | `GameSettings.AddPlayerExp` (0xD7BCC0) |
| Car sold / bought / job paid | `GameScript.SellCar` (0xE95330), `GameScript.BuyCar` (0xE95560), `GameScript.EndJob` (0xE95830) |
| Inventory add/remove | `Inventory.Add` (0xC79640), `Inventory.Delete` (0xC79B80) |
| Warehouse add/remove | `Warehouse.Add` (0x91D520/0x91D5A0), `Warehouse.Delete` (0x91D620/0x91D870) |
| Order lifecycle | `OrderGenerator.GenerateNewJob` (0xC5BB40, **host-only**), `TakeJob` (0xC5D5D0), `CancelJob` (0xC5F9F0) |
| Upgrade purchased | `UpgradeSystem.UnlockUpgrade` (0xB352A0) |
| Difficulty set | `DifficultyManager.SetDifficultyLevel` (0x879120) |
| Save written | `GameDataManager.Save(int)` (0x102BB00) — good point to also persist/snapshot shared state |
| Save loaded | `GameDataManager.LoadProfile()` (0x102C180) and the `GameSaveLoadedArgs`/`OnGameSaveLoadedHandler` event |

**Determinism warning:** `OrderGenerator.GenerateNewJob`, auction/barn/junkyard generation
(`GameSettings` random tables `DentCar*`, `MissingParts*`, quality chances) and `UIDManager.GetNewUID()`
(RVA 0x851C10, allocates item UIDs) use RNG. In co-op, run all generation on the host and replicate the
results (including assigned UIDs) rather than re-rolling on each client, or clients will desync.

**Single-writer rule:** because money/level/inventory are static/singleton globals with no per-player
partitioning, the mod must funnel all mutations through the host. On clients, suppress the local
`GameSettings.*` writes and apply only host-authoritative updates to avoid double-spends.
