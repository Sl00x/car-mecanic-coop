# Entity Identity (§5)

Unity instance ids / memory addresses are NOT stable across machines. The co-op model uses stable,
deterministic network identities (pure types in `shared/Coop/Identity.cs`, unit-tested).

## VehicleKey (§5.1)
`VehicleKey = { string Config, int Place }` → deterministic 64-bit `Handle` (FNV-1a of `"{Config}#{Place}"`).

- Same car config + garage place → same handle on every machine, survives scene reload / reconnect.
- `Config` = `CarLoader.carToLoad` (e.g. `car_vallsen2040`); `Place` = `CarLoader.GetPlaceNo()`.
- **Limitation:** a garage *move* changes `Place` → the handle changes. The spec's answer is a
  host-owned registry that re-keys on move / persists the id in the save. Not built yet; today we
  assume the car stays on its lift for a session (the common case).

## PartSlot (§5.2)
`PartSlot = { VehicleKey Vehicle, string Slot }`. CMS part keys are already machine-stable strings
(`hood`, `headlight_left`, `engine_r4_sohc_wzdluzny/r4_blok(0)`), so the slot id = that key. The slot's
*occupancy* changes; the slot id doesn't. A full `PartInstanceId` (distinguishing two identical parts as
they move between car and inventories) is modelled by `PartData` but not yet threaded through inventory.

## Resolution (§5.3)
`PartSync` maps the current `CarLoader` → `VehicleKey` and keys the authoritative `VehiclePartState` by it.
`CarRefs.LoadedCar()` resolves the live `CarLoader` robustly (scans `GameManager.CarLoadersInScene` when
`GameScript.GetLastCarLoader()` is null). Mappings are implicitly rebuilt on scene/car load because the
key is derived from live `CarLoader` data, not cached instance ids.

## Determinism guarantees (unit-tested, `FakePeer -- unittest`)
- Equal inputs → equal key/handle/hashcode.
- Different place or config → different handle.
- `PartSlot` equality/hashcode stable across machines.
