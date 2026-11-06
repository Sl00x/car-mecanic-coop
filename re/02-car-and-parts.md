# CMS2021 — Car + Part/Assembly System (co-op sync RE)

Game: Car Mechanic Simulator 2021, Unity 2020.3.49f1, IL2CPP.
Source: `re/output/dump.cs` (Il2CppDumper). All offsets are IL2CPP field offsets on the managed object; RVA/Offset are from the dump method comments. Line numbers below refer to `dump.cs`.

TL;DR of the model:
- A part instance = a `PartScript` MonoBehaviour on a GameObject inside the loaded car's transform tree.
- The bolts/screws holding a part on = the `MountObject[] MountObjects` array on that `PartScript`. Each `MountObject` is one bolt.
- The authoritative serialized state of a part = the `PartData` struct (built from a `PartScript`), which bundles condition, paint, dust, tuning **and** the bolts (`MountObjectData`).
- The whole car snapshot = `NewCarData` (holds `List<PartData>`, `List<BodyPartData>`, fluids, engine, wheels, paint, etc.). This is exactly the save file, and is the natural "full car state" payload for co-op.
- **Stable cross-player part identity = the GameObject hierarchy path within the car root** (`PartData.Path`, produced by `PartScript.GetGameObjectPathWithoutRoot()`). Both clients load the same car model/config, so the path is deterministic.

---

## 1. `PartScript` anatomy

`public class PartScript : MonoBehaviour` — TypeDefIndex 8779, dump line **438736**.
THE central part class. One per removable mechanical part.

### 1.1 Full field table

| Offset | Type | Name | Meaning / sync relevance |
|---|---|---|---|
| 0x18 | bool | `devRepair` | dev-only flag |
| 0x19 | bool | `devBroke` | dev-only flag |
| 0x1A | bool | `On` | part enabled/active |
| 0x1B | bool | `alwaysPosX` | unmount dir helper |
| **0x20** | string | **`id`** | **part type/catalog ID** (matches `PartProperty.ID`; NOT instance-unique — many parts share an id). `GetID()`/`SetID(string)` |
| **0x28** | string | **`tunedID`** | currently-installed tuned variant id (if part was tuned/swapped). `GetTunedID()`/`SetTunedID()` |
| 0x30 | ShadersBackup | `shadersBackup` | render state |
| **0x38** | bool | **`IsUnmounted`** | **mounted/unmounted state. KEY SYNC FIELD** |
| **0x3C** | float | **`Condition`** | **part condition/wear 0..1 (or 0..100). KEY SYNC FIELD** |
| **0x40** | int | **`Quality`** | part quality tier (OEM/performance/etc.). KEY SYNC FIELD |
| **0x44** | bool | **`IsExamined`** | whether the part has been diagnosed/examined. KEY SYNC FIELD |
| **0x45** | bool | **`MountAnimationCompleted`** | mount anim finished flag |
| **0x48** | float | **`Dust`** | dirtiness 0..1. KEY SYNC FIELD |
| 0x4C | bool | `oneClickUnmount` | behaviour flag |
| **0x50** | MountObject[] | **`MountObjects`** | **the bolts/screws holding this part. KEY SYNC (array of bolt state)** |
| 0x58 | PartScript[] | `unblockOnUnmount` | parts unblocked when this is removed |
| 0x60 | string[] | `unblockOnUnmountName` | names variant of above |
| 0x68 | bool | `wasAutomatic` | last action was automatic |
| 0x69 | bool | `canBeUnmount` | gating flag |
| 0x70 | GameObject[] | `enableOnUnmount` | visual swap on unmount |
| 0x78 | GameObject[] | `disableOnUnmount` | visual swap on unmount |
| 0x80 | Vector3 | `bakPos` | backup transform pos |
| 0x8C | Quaternion | `bakRot` | backup transform rot |
| 0xA0 | PartProperty | `partProperty` | link to catalog/shop metadata (price, group, DLC, brand...) |
| 0xA8 | float | `vol` | audio vol |
| 0xAC | Vector3 | `unmountVector` | computed unmount direction |
| 0xB8 | direction | `unmountDirection` | enum dir |
| 0xBC | direction | `bonusMoveDirection` | enum dir |
| 0xC0 | bool | `unmountSpinning` | screw-out spins |
| 0xC1 | bool | `spinClockwise` | screw-out spin dir |
| 0xC8 | string | `sendMessage` | gameplay trigger msg on mount |
| 0xD0 | string | `sendMessageOnHide` | gameplay trigger msg on unmount |
| 0xD8 | bool | `checkSendMessageInEngine` | gating |
| **0xDC** | FluidRefillLockType | `FluidRefillLockType` | which fluid this part gates (drain/refill) |
| 0xE0 | bool | `canUpdate` | update gating |
| 0xE1 | bool | `markRed` | UI highlight |
| 0xE2 | bool | `markImportantPart` | UI highlight |
| 0xE3 | bool | `mountWasCanceled` | mount undo flag |
| 0xE4 | bool | `markedRed` | UI state |
| 0xE8 | List\<PartScript\> | `blockedBy` | parts that must be removed first (assembly ordering/dependency) |
| 0xF0 | float | `tuningBonus` | tuning bonus value (see `TuningBonus` prop) |
| 0xF4 | int | `blockedNo` | count of blockers |
| 0xF8 | Highlighter | `ho` | render highlight |
| 0x100 | bool | `MouseOver` | local input state (do NOT sync) |
| 0x101 | bool | `mountMode` | currently in mount-preview mode (local) |
| 0x102 | bool | `replacedShader` | render state |
| 0x108 | Transform | `customPivotForUnmount` | anim pivot |
| 0x110 | GameObject[] | `hideWhenUnmontingMounting` | visual |
| 0x118 | bool | `forInventory` | is an inventory ghost |
| 0x119 | bool | `canMountUnmountOnlyOnCarLoader` | gating: only actionable when on a car |
| 0x11A | bool | `canUnmountSingle` | gating |
| 0x120 | List\<PartScript\> | `unmountWith` | parts that come off together |
| 0x128 | PartScript | `unmountWithMainObject` | group master |
| 0x130 | bool | `unmountWithSeparate` | group flag |
| 0x138 | PartScript | `forcePartGroupFromParent` | grouping |
| 0x140 | int | `ForcePartGroup` | grouping id |
| 0x144 | int | `cullLevel` | LOD |
| 0x148 | GameMode | `GameMode` | cached singleton |
| 0x150 | GameScript | `GameScript` | cached singleton |
| 0x158 | GameInventory | `GameInventory` | cached singleton |
| 0x160 | UIManager | `UIManager` | cached singleton |
| 0x168 | SoundManager | `SoundManager` | cached singleton |
| 0x170 | Cursor3D | `Cursor3D` | cached singleton (input) |
| 0x178 | CameraManager | `CameraManager` | cached singleton |
| 0x180 | ToolsManager | `ToolsManager` | cached singleton |
| 0x188 | GamepadMountObject | `gamepadMountObject` | input |
| **0x190** | Color | `currentColor` | paint color. SYNC (paint) |
| **0x1A0** | bool | `IsPainted` | painted flag. SYNC |
| **0x1A1** | PaintType | `CurrentPaintType` | paint type enum. SYNC |
| **0x1A4** | PaintData | `CurrentPaintData` | paint data struct. SYNC |
| 0x1B8 | bool | `isFluidContainer` | cached |
| 0x1B9 | bool | `flashing` | UI |
| 0x1C0 | Action | `OnHidePart` | event: fired when unmount starts |
| 0x1C8 | Action | `OnHidePartFinished` | event: unmount anim done |
| 0x1D0 | Action | `OnMountFinished` | event: mount anim done |
| 0x1D8 | Action | `OnSelectToUnMount` | event |
| 0x1E0 | Action | `OnShow` | event |

Property: `float TuningBonus { get; set; }` → `get_TuningBonus()` (RVA 0xFF1F70), `set_TuningBonus(float)` (RVA 0xFF1F80).

### 1.2 How a part is uniquely identified across the car tree

Three identifiers exist; only one is instance-unique:

1. **`id` (0x20)** — part TYPE id (catalog key, same as `PartProperty.ID`). Shared by all instances of that part type. NOT unique. `string GetID()` (RVA 0x436720), `void SetID(string)` (RVA 0x413610), `string GetIDWithTuned()` (RVA 0xFF25F0), `string GetTunedID()` (RVA 0x436730).
2. **GameObject hierarchy path (INSTANCE-UNIQUE)** — `string GetGameObjectPathWithoutRoot()` (RVA 0xFEDxxx on `PartGroup`; on `PartScript`: `string GetGameObjectPath()` RVA 0x10065C0 and `string GetGameObjectPathWithoutRoot(StringBuilder)` RVA 0x10069C0). This is the full Transform path from the car root to the part (e.g. `engine/head/camshaft_01`). **This is what the save system uses as the key** — see `PartData.Path`. Lookup by path: `PartScript LookForPart(string _name)` (RVA 0xFF3780).
3. **`int GetUniqueID(string carName, string engineName)`** (RVA 0x1009920) and `int GetUniqueIDFromPartName(string carName, string engineName)` (RVA 0x1009890) — produce an int hash (car+engine+part scoped). Also present on `PartModule.GetUniqueID` and `CarbModule.GetUniqueIDFromPartName`. Useful as a compact network key, but derived from the name/path.

**Recommended co-op key: the GameObject path (option 2).** It is deterministic given the same car model + config version, it is already the save key, and it survives reloads. Compress it to `GetUniqueID`'s int hash on the wire if bandwidth matters, keeping a path↔hash table per loaded car.

---

## 2. Mount / unmount / screw lifecycle (hook points)

### 2.1 Part install / remove (player-initiated entry points)
These `Action*`-prefixed methods are the player-click entry points — the primary hooks:

- `void ActionMount(bool showMenu)` — RVA 0x1000B10 — player installs the selected part. Starts `DoMount()`.
- `void ActionUnMount()` — RVA 0xFFF900 — player removes this part.
- `IEnumerator DoMount()` — RVA 0x1001270 — coroutine running the mount animation; sets `MountAnimationCompleted`, invokes `OnMountFinished`.
- `void MountByGroup(bool instantSet)` — RVA 0xFF5110.
- `IEnumerator UnMountByGroup(bool b, bool instant = false, float delay = 0)` — RVA 0xFF5040.
- `void FastMount()` — RVA 0xFFF810 / `void FastUnmount()` — RVA 0xFFF680 — instant (no anim); good for applying remote state without animation.
- `void UndoMounting()` — RVA 0xFF8900 / `void UndoUnMounting()` — RVA 0xFF8B00 — rollback (use to reconcile when two players grab the same part).
- `void Show()` RVA 0x1002D30 / `IEnumerator Hide()` RVA 0x10030C0 / `IEnumerator ShowMounted()` RVA 0x1002650 — visual show/hide.
- Save-driven (no anim, idempotent — IDEAL for applying remote full-state):
  - `void HideBySavegame(bool withUnmountWith, CarLoader carLoader)` — RVA 0xFF5CD0 — set part to UNMOUNTED per save.
  - `void ShowBySaveGame()` — RVA 0xFF6820 — set part to MOUNTED per save.
  - `void Clone(PartData partData)` — RVA 0xFF4250 — **apply a full PartData onto this PartScript** (condition, dust, paint, tuned id, bolts). This is the master "apply remote state" call.
- Preview helpers (local UI, usually not synced): `ShowPreviewToMount`, `ShowGroupPreview`, `ShowPreview`, `HidePreview`.
- Blocking/dependency: `bool IsBlocked()` (RVA 0xFF1F90), `void BlockPart()`/`void UnBlockPart()`, `void AddToBlockedBy(PartScript)`.

Flags that change across the lifecycle: `IsUnmounted` (0x38), `MountAnimationCompleted` (0x45), `mountMode` (0x101), `mountWasCanceled` (0xE3). Events fired: `OnMountFinished`, `OnHidePart`, `OnHidePartFinished`, `OnSelectToUnMount`, `OnShow`.

### 2.2 Condition / dust / paint / tuning setters (hook these for state edits)
- `void SetCondition(float newCondition, bool updateSolidValue = true)` — RVA 0xFF8F60 — **primary condition setter**.
- `void SetConditionNormal(float newCondition)` — RVA 0xFF8EF0.
- `void UpdateDust(float newDust, bool forceSet = false)` — RVA 0xFF45F0 — dirtiness.
- `void SetColor(Color)` RVA 0xFF2790 / `void SetAllowedColor(AllowedColor)` RVA 0xFF2A30 — paint.
- `void Examine(bool instant)` — RVA 0xFFF6B0 — mark examined/diagnosed (sets `IsExamined`).
- `bool TunePart(string tunePart)` RVA 0xFFDC70 / `bool TunePart(string tunePart, bool shouldResizeWheel)` RVA 0xFFDC80 — swap to tuned variant (changes `tunedID`).
- `void ChangeTireType(string tireName)` — RVA 0xFFF390.
- `void SetMountObjectData(MountObjectData data)` — RVA 0xFF4680 — apply bolt array state.
- `MountObjectData GetMountObjectDataForSave()` — RVA 0xFF47E0 — read bolt array state.
- Fluids: `bool CanDrainFluid(CarLoader)` RVA 0x1004DA0, `bool ZeroFluid(CarLoader)` RVA 0x1005500, `bool IsFluidContainer()` RVA 0xDF6CE0.

### 2.3 Screw / unscrew a single bolt (player-initiated)
A bolt is a `MountObject`. Player acting on it (with ratchet/socket/wrench — note enum `AlternativeDescriptionID.BoltsUnscrew = 21`, `BoltsTighten = 22` at line ~489134) calls:
- `void MountObject.Action()` — RVA 0x9C6920 — **THE toggle: tighten↔loosen one bolt.** Primary bolt hook.
- `void MountObject.PrepareToMount()` — RVA 0x9C6CC0.
- `void MountObject.SetFullMountPosition()` RVA 0x9C5B80 / `void SetFullUnmountPosition()` RVA 0x9C58F0 — snap bolt fully in/out (no anim).
- `void MountObject.UpdateCondition(float newCondition)` — RVA 0x9C5160 — bolt wear.
- `bool MountObject.ActionFinish()` RVA 0x9C58C0, `bool IsUnmounted()` RVA 0x9C58B0, `float GetMountState()` RVA 0x9C5890.
- `IEnumerator MountObject.PlayStuckAnimation()` — RVA 0x9C5E10 — stuck/seized bolt.

A part can only be unmounted once all its `MountObjects` are unmounted; `PartScript.ActionUnMount` checks this. `CarLoader.CheckCarPartsBolts()` (RVA 0x509F70) verifies all required bolts are present.

---

## 3. Car model (`CarLoader`)

`public class CarLoader : MonoBehaviour` — TypeDefIndex 8553, dump line **427981**.
`public class CarLoaderExtended : CarLoader` — TypeDefIndex 8494, line 425558 — the editor/config-authoring subclass (loads `PartTypes`, suspension/licence types, `LoadConfig`/`SaveConfig`). Runtime cars use the base `CarLoader`.

### 3.1 Key `CarLoader` fields (identity + part containers)
| Offset | Type | Name | Meaning |
|---|---|---|---|
| 0x18 | string | `carToLoad` | car model id to load |
| 0x20 | int | `ConfigVersion` | config/version number (must match across players) |
| 0x24 | int | `placeNo` | garage slot |
| 0x49 | bool | `SaveComplete` | |
| 0x50 | LicensePlatesData | `LicensePlatesData` | |
| 0x78 | string | `cachedName` | |
| **0x80** | PartScript[] | **`partScriptCache`** | **cached flat array of ALL PartScripts in the car.** `CachePartScripts()` fills it, `GetPartScriptCache()` returns it |
| 0x88 | bool | `partScriptsCached` | |
| **0x8C** | Guid | `guid` | **per-car-instance GUID** (`GetGUID()` RVA 0x424C20) — good network car id |
| 0x9C | ToolsData | `ToolsData` | installed tools state |
| 0xA8 | FluidsData | `FluidsData` | all fluids (oil/brake/coolant/PS/washer) |
| 0xD0 | int | `CurrentUsedFluidId` | |
| 0xD4 | CarFluidType | `CurrentUsedFluid` | |
| 0xD8 | CarInfoData | `CarInfoData` | mileage etc. |
| 0x178 | GameObject | `root` | **car root GameObject** (paths are relative to this; `GetRoot()` RVA 0x4CD390, `GetRootTransform()` RVA 0x4CD3B0) |
| 0x180 | GameObject | `model` | mesh model |
| 0x1A0 | Color | `color` | body color |
| 0x1C0 | PaintData | `paintData` | |
| **0x1E8** | List\<CarPart\> | **`carParts`** | body panels / paintable car parts |
| **0x1F0** | List\<BonusPart\> | `bonusParts` | bonus/cosmetic parts |
| 0x1F8 | List\<CarLightParams\> | `carLightParts` | |
| 0x290 | string | `m_model` | model name |
| 0x2B0 | string | `m_carBrand` | brand (`CarBrand` prop) |
| 0x510 | EngineParams | `EngineParams` | engine config |
| 0x558 | EngineData | `EngineData` | engine tuning/performance snapshot |
| **0x5C8** | List\<Part\> | **`Parts`** | mechanical parts registry |
| 0x5D0 | CarWheelsData | `WheelsData` | wheels |
| 0x652 | bool | `CanMountUnmountBodyParts` | |
| 0x658 | Action | `OnSwitchCarPart` | event |

### 3.2 Enumerating all parts of a loaded car
- `void CachePartScripts()` (RVA 0x4C8580) → `PartScript[] GetPartScriptCache()` (RVA 0x4C8630) — **flat list of every PartScript**. Iterate this to snapshot/apply all part state.
- `List<Part> GetParts()` (RVA 0x4CC7A0), `List<CarPart> GetCarParts()` (RVA 0x4CC7B0) — mechanical-parts and body-parts registries.
- `void EnablePartScript(string id, bool enable)` (RVA 0x4CC870), `void EnablePartScripts(bool)` (RVA 0x4CC7C0).
- Lookup: `CarPart GetCarPart(string partName)` (RVA 0x4CA800), `bool GetPart(string partName, out Part part)` (RVA 0x4CA9C0).

### 3.3 Load flow
- `IEnumerator LoadCar(string name)` (RVA 0x4CF010) — load a fresh car.
- `void LoadCarFromFile()` + overloads: `IEnumerator LoadCarFromFile(int index, bool fromParking)` (RVA 0x507730), `IEnumerator LoadCarFromFile(string file)` (RVA 0x5077E0), `IEnumerator LoadCarFromFile(NewCarData carDataCheck)` (RVA 0x5078E0) — **load car + apply full `NewCarData` snapshot**.
- Save: `void SaveCarToFile()` (RVA 0x505810), `void SaveCarToFile(int index, bool toParking)` (RVA 0x505910), `string GetSaveName()` (RVA 0x505890).
- `void ChangeCarVersion(int index)` (RVA 0x4CEF60).
- Body-part ops: `void TakeOffCarPart(string partName)` (RVA 0x4FC710), `IEnumerator TakeOffCarPart(string name, bool off)` (RVA 0x4FC830), `void SwitchCarPart(string name[, bool instant])`, `void SwapCarPartInstant(string from, string newPartId)` (RVA 0x4FC740), `bool CanTakeOffCarPart(string, out TakePartOffLockReason)` (RVA 0x4FC940), `void ExplodeCar()` (RVA 0x5003A0).

### 3.4 The full car snapshot type — `NewCarData`
`public struct NewCarData` — TypeDefIndex 8725, dump line **437009**. This IS the save record and the natural co-op "full state" payload.
Fields: `string carToLoad` (0x0), `string UId` (0x8), `int index` (0x10), `float[] color/factoryColor`, `PaintType factoryPaintType`, `bool customerCar`, `int orderConnection`, `ToolsData TooolsData` (0x30), `FluidsData FluidsData` (0x38), `string engineSwap` (0x60), **`List<BodyPartData> BodyPartsData` (0x68)**, **`List<PartData> PartData` (0x70)**, wheel arrays (`tiresET`/`wheelsWidth`/`rimsSize`/`tiresSize`), `int configVersion` (0x98), `LicensePlatesData` (0xA0), `EngineData EngineData` (0xC8), gear ratios, `CarInfoData` (0x114), headlamp/wheel alignment, `bool LightsOn` (0x140), `BonusPartsData` (0x148), `PaintData` (0x178), `TuningData ecuData` (0x198), `int measuredDragIndex` (0x1B0). Methods: `Serialize(BinaryWriter, byte saveVersion)`, `Deserialize(BinaryReader, byte)`, `ReadPartsData`, `ReadBodyPartsData`, `Clone()`, `UpdateBodyPartsData(CarLoader)`.

---

## 4. Bolt / screw model

### 4.1 Runtime: `MountObject`
`public class MountObject : MonoBehaviour` — TypeDefIndex 8711, dump line **436583**. One instance = one bolt/screw/nut. Held in `PartScript.MountObjects[]`.

| Offset | Type | Name | Meaning |
|---|---|---|---|
| 0x18 | bool | `canBeUnmount` | gating |
| **0x1C** | float | `mountState` | 0 = fully unmounted … 1 = fully mounted (continuous, animated). `GetMountState()` |
| **0x20** | float | `Condition` | bolt condition/wear. `UpdateCondition(float)` |
| 0x24 | Vector3 | `oldPos` | mounted position |
| 0x30 | Vector3 | `posUnMount` | unmounted position |
| 0x3C | Quaternion | `oldRot` | mounted rotation |
| 0x4C | float | `length` | travel length |
| 0x50..0x6C | Vector3/Quaternion | `oldPosChild`/`oldRotChild`/`childPosUnMount` | child transform anim |
| 0x78 | bool | `alternativeSFX` | |
| 0x80 | Renderer[] | `renderers` | |
| 0x88 | Highlighter | `ho` | |
| **0x90** | bool | `unmounted` | discrete bolt-is-out flag. `IsUnmounted()` |
| 0x91 | bool | `reverseMode` | |
| 0x92 | bool | `mouseOver` | local input |
| 0x93 | bool | `canAction` | gating |
| **0x94** | bool | `IsStuck` | bolt seized/stuck (needs extra effort). SYNC |
| 0x98 | Transform | `child` | |
| 0xA0 | BoxCollider | `boxCollider` | |
| 0xA8 | bool | `PlayingStuckAnim` | |
| 0xA9 | bool | `canUpdate` | |

Key methods: `Action()` (toggle), `PrepareToMount()`, `SetFullMountPosition()`/`SetFullUnmountPosition()`, `UpdateCondition(float)`, `IsUnmounted()`, `GetMountState()`, `ActionFinish()`, `PlayStuckAnimation()`, `SetCanBeUnmount(bool)`, `SetCanAction(bool)`.

### 4.2 Serialized: `MountObjectData`
`public class MountObjectData` — TypeDefIndex 8731, dump line **437238**. Per-part bolt-set save blob (lives inside `PartData.MountObjectData`).
- `string ParentPath` (0x10) — the owning part's path (same key scheme as `PartData.Path`).
- `float[] Condition` (0x18) — one entry per bolt (index = position in `MountObjects[]`).
- `bool[] IsStuck` (0x20) — one entry per bolt.
- `Serialize(BinaryWriter)` / `Deserialize(BinaryReader, byte saveVersion)`.

Note: the save only persists per-bolt `Condition` and `IsStuck`. The mounted/out state of individual bolts is implied by whether the part itself is mounted (`PartData.Unmounted`); the game fully tightens or fully removes bolts when a part is shown/hidden. For co-op you must additionally sync *in-progress* per-bolt `mountState`/`unmounted` while a player is mid-operation (see §6).

### 4.3 Wheel lug bolts: `WheelBolts`
`public class WheelBolts : MonoBehaviour` — TypeDefIndex 8867, line 443231. Procedurally places lug bolts around a wheel: `boltPrefab`, `boltsNumber` (0x2C), `radius`, `angle`, `startingAngle`, `depth`, `boltScaleMod`. Methods `AddBolts()`, `GetBolts()`, `RepositionDepth()`. The individual lug bolts are still `MountObject`s on the wheel's `PartScript`.

---

## 5. Condition / wear / dirt model

Per-part (on `PartScript`): `Condition` (0x3C, float), `Quality` (0x40, int), `Dust` (0x48, float, dirtiness), `IsExamined` (0x44), paint (`IsPainted`/`CurrentPaintType`/`CurrentPaintData`/`currentColor`), tuning (`tunedID`, `TuningBonus`, `TuningData`). Setters: `SetCondition`, `SetConditionNormal`, `UpdateDust`, `Examine`, `SetColor`, `TunePart`. Each bolt carries its own `Condition` + `IsStuck`.

Per body panel: `struct BodyPartData` (TypeDefIndex 8941, line 445343) — `Id`, `Switched`, `Condition` (0xC), `Unmounted` (0x10), `TunedID`, `IsTinted`/`TintColor`/`Color`, `PaintType`/`PaintData`, `Livery`/`LiveryStrength`, `OutsaidRustEnabled`, **`Dent` (0x60)**, `Quality`, **`Dust` (0x68)**, **`WashFactor` (0x6C)**. So body panels additionally track dents, rust, livery and wash state.

Per fluid: `class FluidData` (TypeDefIndex 8944, line 445503) — `Level` (0x10), `Condition` (0x14), ref to `CarFluid`. Container: `struct FluidsData` (TypeDefIndex 8943, line 445429) — `FluidData Oil` + `List<FluidData>` for `Brake`, `EngineCoolant`, `PowerSteering`, `WindscreenWash`. API: `SetLevelAndCondition(level, condition, CarFluidType, id)`, `SetLevel`/`SetCondition`/`AddFluid`/`GetLevel`/`GetCondition`, `GetAvgLevel`/`GetAvgCondition`. Runtime fluid component `class CarFluid : MonoBehaviour` (TypeDefIndex 8892, line 444147): `FluidType`, `int ID`, `bool HasReservoir`, `SetLevel(float)`, `SetCondition(float)`.

Aggregates on `CarLoader` (read-only, derived — do not sync, recompute): `CalcGlobalCondition()`, `GetPartsGlobalCondition()`, `GetBodyCondition()`, `GetInteriorCondition()`, `GetEnginePartsAvgCondition(bool)`, `GetBrakeAvgCondition(string)`, `GetSuspensionAvgCondition(string)`, `CalcCarValue()`/`CalcPartsValue()`/`CalcBodyValue()`, `GetGlobalCondition()`/`GetPartsCondition()`/`GetPanelsCondition()`. Condition tween helpers: `TweenCondition(string carpartname, float targetRust, float time)`, `SetConditionOnBody(...)`, `SetConditionOnDetails(...)`, `UseWelder()`, `UseInteriorDetailingToolkit()`, `UseOilbin()`.

`struct PartData` — TypeDefIndex 8946, line **445553** — the per-part serialized bundle (what `NewCarData.PartData` is a list of):
`string Path` (0x0, **the unique key**), `bool Examined` (0x8), `float Condition` (0xC), `bool Unmounted` (0x10), `int Quality` (0x14), `string TunedID` (0x18), `FloatArrayWrapper Color` (0x20), `bool IsPainted` (0x28), `PaintType PaintType` (0x29), `PaintData PaintData` (0x2C), **`MountObjectData MountObjectData` (0x40, the bolts)**, `float Dust` (0x48), `TuningData TuningData` (0x50). Built via `PartData..ctor(PartScript partScript)` (RVA 0xFECC00); applied via `PartScript.Clone(PartData)`.

`struct TuningData : ISave` — TypeDefIndex 10367, line 505349 — `bool IsTuned` (0x0), `short[] Values` (0x8), `float TuningValue` (0x10). Max-tuning caps: `class PartsMaxTuningData : ScriptableObject` (line 505153) with `TryGetMaxTuning(string partID, out float)`.

Supporting part types:
- `class PartModule : MonoBehaviour` (TypeDefIndex 9651, line 470960) — attaches tunable behaviour to a part: `PartScript partScript` (0x18), `TuningData data` (0x20), `Tune(short[] newValues, float newTuningValue)`, `IsTuned()`, `GetValues()`, `CopyDataTo/From`, `GetUniqueID(carName, engineName)`.
- `class CarbModule : PartModule` (TypeDefIndex 9649, line 470925) — carburetor module.
- `class EcuModule : PartModule` (TypeDefIndex 9650) — `byte stage` + `SetStage(byte)` (ECU tune stage).
- `class PartProperty : IShopItem` (TypeDefIndex 8927, line 445092) — catalog metadata: `ID`, `Price`, `PartGroup`, `ShopGroup`, `ExamineGroup`, `RepairGroup`, `SpecialGroup`, `DLC[]`, `TuningValue`, `BrakesValue`, `IsBody`, `CanPaint`, `LocalizedName`, `Brand`, `CarID`, `IsMod`.
- `class PartGroup : MonoBehaviour` (TypeDefIndex 8772, line 438494) — just a grouping marker (`showPivot`, `GetGameObjectPathWithoutRoot()`).

Engine physics (runtime, NOT assembly state): `class Block` (VehiclePhysics, TypeDefIndex 9431, line 462194) and `class Engine : Block` (TypeDefIndex 9485, line 464028). `Engine` holds live sim inputs `throttleInput`/`clutchInput`/`ignitionInput` and sensor outputs; its performance curve is derived from `EngineData` (which IS in the save). Do not sync `Engine` directly — it is recomputed from the assembled parts + `EngineData`; only driving inputs need syncing if co-op allows driving.

---

## 6. Co-op sync notes

### 6.1 Minimal per-part replicated state
Per `PartScript` (keyed by path), send only what the local player can change:
- `IsUnmounted` (bool) — mounted/removed.
- `Condition` (float), `Quality` (int), `Dust` (float), `IsExamined` (bool).
- Paint: `IsPainted`, `CurrentPaintType`, `CurrentPaintData`, `currentColor`.
- Tuning: `tunedID` (string) + `TuningData` (`IsTuned`, `short[] Values`, `TuningValue`).
- Bolts: for each `MountObject` in `MountObjects[]` → `Condition` (float), `IsStuck` (bool), and — for live co-op — `mountState` (float) + `unmounted` (bool) so a half-unscrewed bolt shows correctly on the other client.

This is essentially `PartData` + the extra live bolt `mountState`/`unmounted` fields. Body panels use `BodyPartData` (adds `Dent`, `WashFactor`, livery, tint). Fluids use `FluidData` (`Level`, `Condition`) per `CarFluidType`+id.

### 6.2 Stable part identifier proposal
Use the **GameObject hierarchy path within the car root** as the canonical key:
- Source of truth: `PartScript.GetGameObjectPathWithoutRoot()` (already the save key `PartData.Path`, and `MountObjectData.ParentPath`).
- Car scope key: `CarLoader.guid` (`GetGUID()`) or the garage `placeNo` / `NewCarData.UId`.
- Prerequisite for determinism: both clients must load the **same** `carToLoad` model at the same `ConfigVersion` (compare `CarLoader.ConfigVersion` / `NewCarData.configVersion` on session join).
- On-wire optimization: hash each path once per car via `PartScript.GetUniqueID(carName, engineName)` (int) and keep a client-side `int → PartScript` table (built from `GetPartScriptCache()`), so part refs are 4 bytes. Bolts are addressed as `(partKey, boltIndex)` where boltIndex is the array index into `MountObjects[]`.

### 6.3 Best hook points to intercept player actions
Patch/wrap these methods; the acting client sends an event, remote clients apply via the no-animation / save-driven variants:

| Player action | Hook (authoritative client) | Apply on remote (no anim / idempotent) |
|---|---|---|
| Install part | `PartScript.ActionMount(bool)` → `DoMount()` | `PartScript.FastMount()` / `ShowBySaveGame()` / `Show()` |
| Remove part | `PartScript.ActionUnMount()` | `PartScript.FastUnmount()` / `HideBySavegame(bool, CarLoader)` |
| Tighten/loosen a bolt | `MountObject.Action()` | `MountObject.SetFullMountPosition()` / `SetFullUnmountPosition()`; or set `mountState` directly for in-progress |
| Bolt wear | `MountObject.UpdateCondition(float)` | same |
| Set part condition | `PartScript.SetCondition(float,bool)` / `SetConditionNormal(float)` | same |
| Dirt | `PartScript.UpdateDust(float,bool)` | same |
| Examine/diagnose | `PartScript.Examine(bool)` | same |
| Paint | `PartScript.SetColor(Color)` / `SetAllowedColor(AllowedColor)` | same |
| Tune / swap variant | `PartScript.TunePart(string[,bool])`, `PartModule.Tune(short[],float)`, `EcuModule.SetStage(byte)` | same + `Clone(PartData)` |
| Body panel take-off/switch | `CarLoader.TakeOffCarPart(...)`, `SwitchCarPart(...)`, `SwapCarPartInstant(...)` | same (instant overloads) |
| Fluid drain/refill | `CarFluid.SetLevel/SetCondition`, `FluidsData.SetLevelAndCondition(...)`, tool coroutines `ToolsManager.UseOilDrain`, `FluidRefill` | same setters |
| Full car state (join / resync) | `CarLoader.SaveCarToFile` builds `NewCarData` | `CarLoader.LoadCarFromFile(NewCarData)`; per-part `PartScript.Clone(PartData)` |

- **Catch-all apply path:** `PartScript.Clone(PartData)` applies condition + dust + paint + tunedID + bolts (`SetMountObjectData`) in one call. Combined with `ShowBySaveGame`/`HideBySavegame` for mount state, this is the cleanest "set remote part to state X" primitive.
- **Full-state snapshot for late-join / desync recovery:** serialize `NewCarData` (`Serialize(BinaryWriter, saveVersion)`) and apply with `LoadCarFromFile(NewCarData)`. This replays the entire assembled car, fluids, engine, wheels, paint.
- **Concurrency / conflict:** `PartScript.IsBlocked()` + `blockedBy` enforce assembly ordering; `UndoMounting()`/`UndoUnMounting()` let you roll back a losing client when two players grab the same part. Suppress sync of purely-local fields: `MouseOver`, `mountMode`, highlight/`markRed`, cached singletons, renderers.
- **Input source:** `Cursor3D` (TypeDefIndex 8575, line 429946) + `ToolsManager` (TypeDefIndex 8708, line 436350) drive which `PartScript`/`MountObject` the player is acting on — hook there if you need the originating player context, but the `Action*` methods above are the clean semantic boundary.
