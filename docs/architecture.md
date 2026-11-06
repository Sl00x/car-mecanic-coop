# CarMecaSim — Multiplayer Architecture

## Goal
Shared-garage co-op for Car Mechanic Simulator 2021 (CMS21): 2+ players in the same garage,
working on the same car(s). Delivered as a **MelonLoader mod** + a **launcher** that hosts/joins.

## Constraints that shaped the design
- CMS21 is **IL2CPP** and **single-player by construction** — no existing netcode, no ECS, no
  authoritative tick loop. State lives in `MonoBehaviour` singletons mutated directly by UI/input.
- We cannot change the game's code; we **hook** methods (Harmony via MelonLoader) and drive state.
- **Steamworks.NET** is already shipped and Steam is initialized by the game → we reuse it for
  transport (no port-forwarding: Steam relay + NAT punch).

## Topology — host-authoritative, star
```
            (Steam P2P / relay)
   Client A  ───────────┐
                         ▼
                      HOST  ── owns ground-truth game state (money, car assembly, orders, inventory)
                         ▲
   Client B  ───────────┘
```
- **Host** = one player's running game instance. Its `GameManager`/save IS the session state.
- **Clients** send *intents* ("I loosened bolt #42 on part #7", "I bought part X"); the host
  validates, applies locally, and **broadcasts the resulting authoritative state delta** to all.
- Clients never mutate authoritative state on their own; they apply deltas from the host and
  render presence of other players. This avoids desync and duplicate money/parts.
- Optimistic local prediction for *presence* only (movement/look), never for economy/assembly.

## Transport — Steam
- **Lobby**: `SteamMatchmaking.CreateLobby` (host) / `JoinLobby` (client). Lobby carries metadata
  (host SteamID, mod version, game version) and is how clients discover the host SteamID.
- **Messages**: `SteamNetworkingMessages.SendMessageToUser` / `ReceiveMessagesOnChannel`
  (modern, connectionless, relay-capable) — fallback to legacy `SteamNetworking` P2P if needed.
- Channels / send-flags:
  - ch 0 `Unreliable (nodelay)` — presence (position/look), high-frequency, latest-wins.
  - ch 1 `Reliable` — actions, economy, orders, chat, full-state snapshots, join/leave.
- We ship our **own Steamworks.NET build** inside the mod (Il2CppInterop makes calling the
  game's bundled managed Steamworks awkward from a mod; a private net6 Steamworks.NET that
  binds the already-loaded `steam_api64.dll` is cleaner). `steam_appid.txt` carries CMS21's AppId.

## Packet model (see protocol.md for wire format)
Every packet: `[u8 type][u16 len][payload]`, payload = compact binary (hand-rolled `BinaryWriter`).
Categories:
| Type group | Reliable? | Direction | Purpose |
|---|---|---|---|
| `Hello`/`Welcome`/`FullState` | reliable | both | handshake + initial snapshot on join |
| `Presence` | unreliable | client→host→all | position, look dir, equipped tool, action anim |
| `PartAction` | reliable | client→host (intent), host→all (confirm) | install/remove/screw/loosen/condition |
| `CarOp` | reliable | host→all | load/unload car, lift up/down, fluids |
| `Economy` | reliable | client→host→all | buy/sell, money set (authoritative) |
| `Order` | reliable | host→all | order accepted/updated/completed |
| `Inventory` | reliable | host→all | add/remove inventory item |
| `Chat` | reliable | any→all | text chat |
| `Ping`/`Bye` | reliable | both | keepalive / graceful leave |

## Module layout (mod/)
```
Net/        SteamTransport, LobbyManager, PacketReader/Writer, PacketRouter
Core/       Mp (singleton), Session (host|client|offline), PlayerRegistry
Sync/       PresenceSync, PartSync, EconomySync, OrderSync, InventorySync, CarSync
Game/       thin wrappers over hooked game classes (GameManager, PartScript, ...) — filled from RE
Hooks/      Harmony patches that emit intents / apply remote state
UI/         in-game overlay: lobby status, player list, chat (MelonLoader IMGUI or uGUI)
```

## Join flow
1. Client joins Steam lobby → opens P2P session to host → sends `Hello{modVer,gameVer,name}`.
2. Host validates versions → replies `Welcome{yourPlayerId, roster}` → sends `FullState`
   (money, loaded car(s) + full per-part state, orders, inventory, garage config).
3. Client applies `FullState` (loads same car, sets each part's state), spawns proxies for
   existing players, then enters live sync. Host announces the new player to others.

## Determinism / conflict rules
- Only the host generates orders, prices, RNG outcomes, and final money. Clients mirror.
- Part actions are **serialized by the host**: concurrent edits to the same part are applied in
  host-receive order; the host's confirm is the truth and overrides client-side optimistic UI.
- Each part has a **stable network id** (see protocol.md) agreed by all peers from the car's
  deterministic part hierarchy, so "part #N" means the same object on every machine.

## Out of scope for v0 (iterate later)
Test track / dyno racing sync, paintshop fine sync, photo mode, auction house, map/world travel.
v0 target = same garage, same car, parts + bolts + money + orders stay consistent + presence + chat.
