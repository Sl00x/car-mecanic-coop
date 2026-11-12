# CMS 2021 — Reverse Engineering Map

Runtime: **IL2CPP** (Unity 2020.3.49f1), BepInEx 6 + Il2CppInterop. Interop proxies under
`<game>/BepInEx/interop/`. Game code splits across two assemblies:

- `Assembly-CSharp.dll` (~1 MB) — `PartScript`, `MountObject`, UI, menus. REA-inspectable.
- `Assembly-CSharp-firstpass.dll` (~8.8 MB) — `GameManager`, `GameDataManager`, `CarLoader`, `Inventory`,
  `ProfileData`, `SaveGame`. **Too big for REA's managed-metadata budget (67 MB)** → inspect via
  `strings | grep NativeMethodInfoPtr_...` (il2cppinterop encodes each method's full signature in the
  native-pointer field name, e.g. `TakeOffCarPart_Public_Void_String_0` = `public void TakeOffCarPart(string)`).

Confidence legend: **V** = verified live in-game, **S** = signature confirmed from interop metadata,
**H** = hypothesis / not yet exercised.

---

## Save / profile

| Item | Detail | Conf |
|---|---|---|
| Save file | ONE file: `%USERPROFILE%/AppData/LocalLow/Red Dot Games/Car Mechanic Simulator 2021/Save/profile0.cms21b` (~34 KB) + `/Backup/`. Magic bytes `50 4A 4F 4F 4F 54 45 52` ("PJOOOTER") then version `E5 07 05`. | V |
| `GameDataManager.CurrentProfileData` | `ProfileData` getter. In-memory it is NEAR-EMPTY mid-session (blank `Name`, `SerializeToBytes()` → ~1.1 KB). DO NOT serialize it for a snapshot. | V |
| `ProfileData.SerializeToBytes()` → `Il2CppStructArray<byte>` | Produces the same PJOOOTER format as the file, but on the live in-memory object it is empty. | V |
| `ProfileData.DeserializeFromBytes(ref Il2CppStructArray<byte>)` | Reads the PJOOOTER format. Works on the real **file bytes** (34 KB) → use the file, not `SerializeToBytes`. | V |
| `ProfileData.Init()` | Supposed to gather live state; does NOT pull in cars → leaves the profile empty. | V |
| `ProfileData.carsInGarage` → `Il2CppReferenceArray<NewCarData>` | Garage slots (length = slot count, e.g. 5). `NewCarData.carToLoad` = car config name. | S |
| `GameDataManager.LoadInstant(bool)` | Applies money/inventory/jobs after a deserialize. | V (used) |
| Fresh-save trigger | `GameManager` has **no** `SaveGame()`. `SaveGame_Public_Void_0` / `Save_Public_Static_Void_SaveGame_0` exist on some firstpass type (owner TBD). Needed so the snapshot reflects unsaved live changes. | H / TODO |

## CarLoader (per-car controller, firstpass)

| Method | Signature | Use | Conf |
|---|---|---|---|
| `TakeOffCarPart(string)` | `public void` | **Runtime part removal — THE removal entry point.** Visual. Key is a bare part name ("hood", "headlight_left", "door_front_left"). | V |
| `TakeOffCarPart(string, bool)` | `public IEnumerator` | Coroutine/animated removal; fires alongside the Void one (dedup needed). | V |
| `TakeOffCarPartFromSave(string)` | `public void` | Save-replay removal — sets state only, **NO visual rebuild**. Not for live apply. | V |
| `TakeOnCarPartFromSave(string)` | `private void` | Save-replay install — state only, **NO visual mount** (confirmed: hood did not reappear). Runtime visual-mount method still TBD. | V |
| `SwitchCarPart(string)` / `(string,bool)` | `public void` | Swap/toggle a part — candidate for runtime install. | H |
| `LoadCar(string)` | `public` | Loads car config 0 only (not full saved config). | S |
| `LoadCarFromFile(int place, bool)` | `public` (coroutine) | Loads a car's full saved config into a garage place. | V (used) |
| `DeleteCar()` | `public void` (no-arg; also an overload — patch with explicit `Type[0]`) | Removes the car. | V |
| `IsCarLoaded()` / `GetPlaceNo()` | `bool` / `int` | Car loaded? / garage place index. | V |
| `carToLoad` | `string` | Car config name on this loader. | V |
| `CachePartScripts()` / `GetPartScriptCache()` → `Il2CppReferenceArray<PartScript>` | | Enumerate the car's parts. | V |
| `CanTakeOffCarPart(string, ref TakePartOffLockReason)` | `public bool` | Precondition check (bolts, dependencies). | S |
| `IsPartUnmounted(string)` | `public bool` | Query a part's removed state — useful for state extraction. | S |
| `GetCarPart(string)` → `CarPart` / `GetPartInfo(string)` | | Part data by key. | S |

## PartScript (Assembly-CSharp)

| Method | Note | Conf |
|---|---|---|
| `ActionMount(bool)` / `ActionUnMount()` | NOT the user's removal path — hooking them never fired in-game. Do not use for capture. | V (negative) |
| `FastMount()` / `FastUnmount()` | Instant mount/unmount of a single PartScript (no inventory transaction). | S |
| `ActionUnMountGroup(InteractiveObject)` | Grouped-part removal. | S |
| `MountObjects` → bolts; `GetGameObjectPath()` | Per-part bolt list / stable-ish GO path. | S |

## MountObject (bolts)

| Method | Note | Conf |
|---|---|---|
| `Action()` | Bolt step. Hooking it did NOT fire for the parts tested (body panels are boltless click-removal). `Action(int)` does NOT exist on MountObject. | V (negative) |

## Inventory (firstpass) — the removal signal we first saw

Game logs `[Inventory] -> Add() car_vallsen2040-hood` when a part is removed (goes to inventory) and
`-> Delete()` on sell/move. The inventory key is `"<carConfig>-<partName>"`. This is how we discovered
that removal flows through `CarLoader.TakeOffCarPart`, not PartScript. **TODO:** map `Inventory.Add/Remove`
signatures for proper part-data (condition/props) capture (section 15 of the spec).

## Scene / managers

| Item | Note | Conf |
|---|---|---|
| `GameManager` (firstpass) | `CarLoadersInScene` → `List<CarLoader>`; `GameDataManager` getter. No `SaveGame()`. | V |
| `GameScript.GetLastCarLoader()` | Current car on the lift — **can return null** even when a car is loaded; fall back to scanning `CarLoadersInScene` for `IsCarLoaded()` (see `CarRefs.LoadedCar()`). | V |
| Car config names observed | `car_vallsen2040`, `car_hinatamoonmk1`, `car_emdenjager`; `car_wash` = the wash station (not a vehicle). | V |

## Open items (needed for the full engine)

- Runtime **visual mount** method (install a part with rebuild) — `TakeOnCarPartFromSave` is state-only.
- `SaveGame()` owning type (for fresh snapshots).
- Part **data** extraction (condition, variant, tuning, color) — `GetCarPart`/`GetPartInfo` + `CarPart` fields.
- Door / hood / trunk **open-state** methods (section 9).
- Lift / engine-stand / tool state methods (section 16).
- Stable **vehicle identity** across machines — see ENTITY_IDENTITY.md.
