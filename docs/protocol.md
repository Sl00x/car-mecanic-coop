# CarMecaSim — Network Protocol & Sync Spec (v0)

Synthesized from the RE docs in `re/01..04`. Everything maps onto the game's existing
save structs so we reuse its (de)serializers wherever possible.

## 0. Transport
- Mod ships its **own Steamworks.NET** (net6) and P/Invokes the already-loaded native
  `steam_api64.dll`. Wait for `SteamManager.Initialized == true`; never call `SteamAPI.Init` again.
  `steam_appid.txt = 1190000` guarantees binding to CMS21's app.
- **Lobby**: `SteamMatchmaking.CreateLobby(k_ELobbyTypeFriendsOnly|Public, maxMembers)`.
  Host sets lobby data: `cms_mp_ver`, `game_ver`, `host_name`. Clients `JoinLobby`, read host
  SteamID from `GetLobbyOwner`.
- **Messages**: `SteamNetworkingMessages.SendMessageToUser(identity, data, flags, channel)` +
  `ReceiveMessagesOnChannel`. Pump in a MelonLoader `OnUpdate`. Channels:
  - ch0 presence → `k_nSteamNetworkingSend_UnreliableNoDelay`
  - ch1 control/state/economy/chat → `k_nSteamNetworkingSend_Reliable`

## 1. Wire format
Little-endian. Frame:
```
[u8  type]        // PacketType
[u8  flags]       // bit0 = isHostAuthoritative(confirm), rest reserved
[u16 bodyLen]
[body...]
```
Primitives: `u8,i32,u32,f32,i64(SteamID),string(=u16 len + UTF8)`. Vec3 = 3×f32. Quat packed as yaw/pitch f32 for presence. Reuse game serializers for big blobs (see §3).

## 2. PacketType enum
```
0  Hello        C→H   {modVer:str, gameVer:str, name:str, steamId:i64}
1  Welcome      H→C   {assignedPlayerId:u8, roster:[(id,name,steamId)], hostSettingsHash:i32}
2  Reject       H→C   {reason:str}                       // version mismatch etc.
3  FullState    H→C   {profileBlob}                      // §3 — initial snapshot
4  PlayerJoin   H→all {id,name,steamId}
5  PlayerLeave  H→all {id}
6  Presence     C→H→all {id,pos:Vec3,yaw,pitch,tool:u8,mode:u8,heldPartKey:i32} // unreliable
7  PartAction   C→H (intent) / H→all (confirm) {§4}
8  CarOp        H→all {§5}
9  Economy      C→H (intent) / H→all (absolute) {§6}
10 Order        H→all {§7}
11 Inventory    H→all {op:u8(add|del), itemBlob}         // Warehouse.Add/Delete
12 Chat         any→all {fromId:u8, text:str}
13 Ping         both  {t:i64}
14 Bye          both  {}
```
Rule: clients emit **intents** (6,7,9); the host validates, executes the real game method, and
re-broadcasts the **authoritative** result (flags.bit0=1). Clients apply only host-authoritative
packets for economy/parts/orders/inventory; presence is applied directly (peer-owned).

## 3. Snapshot (FullState) — reuse the save serializer
- Host: `GameDataManager.CurrentProfileData.SerializeToBytes()` (RVA 0x987D30) → compress (deflate)
  → send as `FullState`. Contains money/xp/level, all `NewCarData` (cars in garage/parking),
  inventory/warehouse, jobs, upgrades, garage customization, player pos.
- Client: decompress → `ProfileData.DeserializeFromBytes(ref bytes)` (0x988750) → drive a normal
  load so the client's garage mirrors the host (same cars loaded, same part states).
- Save version byte must match (`CurrentSaveVersion = 5`); abort join on mismatch (→ Reject).

## 4. PartAction — the core co-op loop
Part identity: `partKey:i32 = PartScript.GetUniqueID(carName,engineName)` (0x1009920), derived from
`GetGameObjectPathWithoutRoot()` (0x10069C0). Bolts: `boltIndex:u8` into `MountObject[] MountObjects`.
`carGuid:i64` (`CarLoader.guid`) disambiguates multiple loaded cars.
```
PartAction body:
  [u8 action][i64 carGuid][i32 partKey]
  action switch:
   0 Mount        {}                              // ActionMount(true)/DoMount → remote FastMount()
   1 Unmount      {}                              // ActionUnMount → remote FastUnmount()
   2 BoltStep     {boltIdx:u8, mountState:f32, unmounted:u8, stuck:u8}  // live MountObject.Action()
   3 BoltFinal    {boltIdx:u8, fullMounted:u8}    // SetFullMountPosition/SetFullUnmountPosition
   4 Condition    {cond:f32}                      // SetCondition
   5 Dust         {dust:f32}                      // UpdateDust
   6 Examine      {}                              // Examine(true)
   7 Paint        {color:4xf32, paintType:u8, paintData:i32}
   8 Tune         {tunedID:str}                   // TunePart
   9 CloneFull    {partDataBlob}                  // PartScript.Clone(PartData) — catch-all resync
```
Apply order: host serializes concurrent edits to the same `partKey` in receive order; its confirm
wins. Half-unscrewed bolts stream action=2 at ~10 Hz while the tool is active; action=3 on finish.
Fallback/repair: periodic or on-desync `CloneFull` using `PartData` (serialize one part, 0xFF4250).

## 5. CarOp
```
[u8 op]
 0 LoadCar   {newCarDataBlob, carGuid}   // host loaded a car → clients CarLoader.LoadCarFromFile(NewCarData)
 1 UnloadCar {carGuid}
 2 Lift      {carGuid, height:f32|state:u8}   // CarLifter
 3 Fluid     {carGuid, fluidType:u8, level:f32, cond:f32}  // CarFluid/FluidData
 4 BodyPart  {carGuid, path:str, dent:f32, washFactor:f32, rust:f32, livery...}
```

## 6. Economy (host-authoritative; clients send intent only)
```
[u8 kind]
 0 MoneySet    {money:i32}            // host broadcasts absolute after any mutator
 1 XpSet       {xp:i32, level:i32, scraps:i32}
 2 BuyPart     {shopItemId, price}    // intent; host runs ShopBuyWindow.BuyItem → MoneySet+Inventory
 3 SellPart    {itemKey}              // intent
 4 BuyCar      {carId, ...}           // intent; host GameScript.BuyCar
 5 SellCar     {carGuid}              // intent; host GameScript.SellCar
```
Client-side suppression: when a client's local code path would mutate `GameSettings.PlayerMoney`
or `Warehouse`, the Harmony prefix converts it into an Economy intent and **skips the original**
(return false), waiting for the host's authoritative echo. On the host the originals run normally
and a postfix broadcasts the absolute result.

## 7. Order
```
[u8 kind]
 0 JobList   {jobs:[JobData]}         // host-only GenerateNewJob result; full replace
 1 TakeJob   {jobId:i32}              // host executes OrderGenerator.TakeJob
 2 CancelJob {jobId:i32}
 3 FinishJob {jobId:i32}              // host GameScript.EndJob → also emits Economy MoneySet/XpSet
```
RNG (`UIDManager.GetNewUID` 0x851C10, job generation) runs **only on host**; generated ids/UIDs
are shipped in JobList so clients never roll their own.

## 8. Presence (unreliable, ~15–20 Hz)
Source each peer: pos = `CharacterMotor.tr.position`; yaw/pitch = `FPSCamera.m_Yaw/m_Pitch`;
tool = `ToolsManager.currentUsedTool`; mode = `GameMode.currentMode`; heldPartKey = selected part.
Remote render: spawn a **proxy** (no body mesh exists in game) = capsule + floating nametag +
simple tool indicator; lerp position, set rotation from yaw/pitch. Hide proxy when peer not in
`SceneType.Garage` or `GameMode==None`.

## 9. Co-op activation gate
Only run sync while: `GameScript.Get().CurrentSceneType == SceneType.Garage` (==4) &&
`SceneManager.Get() != null` && `GameMode.currentMode != None`. On scene exit → pause sync, keep
the Steam session; re-FullState on re-entry if drift suspected.

## 10. Anti-desync
- Host authoritative for money/xp/jobs/inventory/part-final-state; clients optimistic only for
  presence and their own in-progress bolt animation (reconciled by host confirm).
- Heartbeat Ping every 2 s; on 10 s silence → drop peer (PlayerLeave).
- Drift repair: host can push a targeted `CloneFull` per part, or a full re-`FullState`.

## v0 acceptance
Two players: join via launcher → same garage → see each other (proxy+chat) → either can
disassemble/assemble the same car with bolt/part/condition staying consistent → buying parts &
completing a job updates both players' money identically, no double-spend.
