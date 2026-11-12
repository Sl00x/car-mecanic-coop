# Harmony Hooks (§11)

Each patch is applied individually (`PatchClassProcessor` per class in `Plugin.cs`) so one bad hook can't
drop the others; failures are logged, the game still starts.

| Target | Kind | Purpose | Double-exec risk | Conf |
|---|---|---|---|---|
| `CarLoader.TakeOffCarPart(string)` | Postfix | capture a part removal (bare key) → `PartSync.OnLocalTakeOff` | HIGH — also fires with the `(string,bool)` overload; deduped by key within 0.5 s | V |
| `CarLoader.TakeOffCarPart(string, bool)` | Postfix | same removal via the coroutine overload | HIGH (see above) | V |
| `CarLoader.LoadCar(string)` | Postfix | host garage changed → delta snapshot; reset canonical part state for the new car | loads during RespawnGarage use `LoadCarFromFile`, NOT `LoadCar`, so no feedback loop | V |
| `CarLoader.DeleteCar()` (empty `Type[0]`) | Postfix | car removed → garage delta (explicit `Type[0]` avoids the ambiguous overload that would drop all patches) | — | V |
| `PartScript.DoMount()` | Postfix | mechanical part MOUNTED → `CarPartSync.OnDoMount` → positional id (type,key,index) (CMS21-Together) | coroutine: fires at enumerator creation, not completion | S (needs in-game) |
| `PartScript.Hide()` (empty `Type[0]`) | Postfix | mechanical part UNMOUNTED (the reliable unmount signal) → `CarPartSync.OnHide` | many `Hide` overloads — `Type[0]` selects the no-arg one | S (needs in-game) |

## Capture vs apply (§11.1)
We hook **after** the real operation (postfix) so we replicate a genuine, succeeded removal — not a UI
click. Applying a remote action calls the same native `TakeOffCarPart`, so to avoid a replication loop
(§11.2) `PartSync.Applying` + a `ReplicationContext` scope (`MutationOrigin.RemoteReplication`) mark the
mutation as remote; `OnLocalTakeOff` early-returns when `Applying`, so it never re-broadcasts.

## Not hooked (negative findings — do NOT use)
- `PartScript.ActionUnMount()` / `ActionMount(bool)` — never fired for the removals we tested.
- `MountObject.Action()` — never fired (body panels are boltless click-removal); `Action(int)` does not
  exist on `MountObject`.

## TODO hooks (need RE / in-game confirmation)
- Runtime **install** (visual mount) — `TakeOnCarPartFromSave` is state-only; capture + apply TBD.
- Door / hood / trunk open-close (§9).
- Interaction START (to make removals a host-validated *request* and enable true distributed locks §10).
- Lift / engine-stand / tool operations (§16).
- `GameManager`/`SaveGame` save trigger for a fresh snapshot.
