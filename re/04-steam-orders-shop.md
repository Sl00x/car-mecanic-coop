# 04 — Steam integration, Orders/Jobs, and Shop/Economy

Reverse-engineering notes for the co-op (Steam P2P) mod.
Source: `re/output/dump.cs` (Il2CppDumper, Unity 2020.3.49f1, IL2CPP x64). All RVA/Offset/VA are into `GameAssembly.dll`. Field offsets are the instance/static layout from the dump.

> Note: this build is IL2CPP, so the managed `DummyDll` only mirrors the *stripped* surface that the game actually references. `GameAssembly.dll` is not in `re/output`, so method bodies could not be decompiled — everything below is signatures + offsets + field semantics from the dump. Internal call chains marked "(inferred)" are strongly implied by the field layout but not byte-confirmed.

---

## 1. SteamManager — init flow, AppId, readiness

### `SteamManager : MonoBehaviour` — TypeDefIndex 8441 (dump line 423203)
This is the **stock Steamworks.NET `SteamManager`** (unmodified rlabrecque boilerplate).

```
// Fields
private static bool s_EverInitialized;                     // 0x0
private static bool m_bInitialized;                        // 0x1
private SteamAPIWarningMessageHook_t m_SteamAPIWarningMessageHook; // 0x18

// Properties
public static bool Initialized { get; }                    // -> returns m_bInitialized

// Methods
public static bool get_Initialized()                       // RVA 0xD9C0E0
private static void SteamAPIDebugTextHook(int nSeverity, StringBuilder pchDebugText) // RVA 0xD9C120
private void Awake()                                        // RVA 0xD9C180
private void OnEnable()                                     // RVA 0xD9C620
private void OnDestroy()                                    // RVA 0xD9C830
private void Update()                                       // RVA 0xD9CB80
public void .ctor()                                         // RVA 0x4AE1D0
```

Standard Steamworks.NET behaviour (matches the stock source):
- `Awake()` → guards on `s_EverInitialized`, calls `Packsize.Test()` / `DllCheck.Test()`, then **`SteamAPI.Init()`** (`Steamworks.SteamAPI.Init`, declared at dump line ~380876; flat import `SteamAPI_Init` at 380971). On success sets `m_bInitialized = true`, `DontDestroyOnLoad(this)`.
- `OnEnable()` → installs the warning-message hook via `SteamClient.SetWarningMessageHook`.
- `Update()` → **`SteamAPI.RunCallbacks()`** (dump line 380890) every frame. This is the callback pump the whole process shares.
- `OnDestroy()` → `SteamAPI.Shutdown()`.

**Steam IS initialized at startup.** The game instantiates it via `GameObject.AddComponent<SteamManager>` (seen in the hierarchy/bootstrap comment at dump line 218567). So the native `steam_api64.dll` + a live `HSteamPipe/HSteamUser` already exist before any mod code runs.

### AppId
`SteamManager` itself has **no** AppId constant. The AppId lives in **`GameSettings`** (static class, TypeDefIndex 8618, dump line 432129):

```
public const uint AppIdDemo     = 1585850;
public const uint AppIdPrologue = 1585850;
public const uint AppIDNormal   = 1190000;   // <-- CMS2021 retail Steam AppID
public static uint AppID;                     // 0x8  (runtime-selected id)
private static string m_buildVersion;         // 0x0
```

So the retail **AppID = 1190000**. `steam_appid.txt` in the game dir will contain this; `RestartAppIfNecessary` (flat `SteamAPI_RestartAppIfNecessary`, 380977) uses it.

### How a mod knows Steam is ready
- Poll **`SteamManager.Initialized`** (static bool property, `get_Initialized` @ 0xD9C0E0) — `true` once `SteamAPI.Init()` succeeded.
- Since the mod brings its *own* Steamworks.NET build, it must NOT call `SteamAPI.Init()` again (that would assert). Just wait for `SteamManager.Initialized == true`, then grab `SteamUser.GetSteamID()` / build networking interfaces against the already-running client. The game's `SteamManager.Update` already pumps `RunCallbacks()`, but the mod should run its *own* callback registration (its Callback<T> instances dispatch through the same global `CallbackDispatcher`).

---

## 2. Steamworks.NET surface present in the dump (transport-relevant)

IL2CPP kept only the Steamworks types the game references. The `Steamworks` namespace in the dump spans ~380180–382600.

### High-level wrapper classes PRESENT (public static class):
| Class | TypeDefIndex | dump line |
|---|---|---|
| `SteamAPI` | 7224 | 380876 |
| `SteamClient` | 7250 | 381644 |
| `SteamApps` | 7249 | 381635 |
| `SteamFriends` | 7251 | 381722 |
| `SteamRemoteStorage` | 7252 | 381752 |
| `SteamScreenshots` | 7253 | 381785 |
| `SteamUGC` | 7254 | 381794 |
| `SteamUserStats` | 7255 | 381848 |
| `SteamUtils` | 7256 | 381878 |

Infra present: `CallbackDispatcher` (7209), `Callback`/`Callback<T>` (7210/7212), `CallResult`/`CallResult<T>` (7213/7215), `CallbackIdentities`, `InteropHelp`, `DllCheck`, `Packsize`, `NativeMethods` (7226, flat P/Invoke), `CSteamAPIContext` (7225).

Structs/enums present: `CSteamID` (7258, line 381919), `AppId_t`, `SteamAPICall_t`, `HSteamPipe`, `HSteamUser`, `CallbackMsg_t`, `SteamAPICallCompleted_t`, `SteamParamStringArray_t`, plus UGC/workshop structs; enums `EResult`, `EAccountType`, `EUniverse`, `EOverlayToStoreFlag`, `ERemoteStoragePublishedFileVisibility`, `EWorkshopFileType`, `EItemUpdateStatus`, `EGamepadTextInput*`.

### Transport wrapper classes ABSENT from the dump (stripped — game doesn't use them):
`SteamUser`, `SteamMatchmaking`, `SteamMatchmakingServers`, `SteamNetworking` (old P2P), **`SteamNetworkingSockets`**, **`SteamNetworkingMessages`**, `SteamNetworkingUtils`, `SteamInput`/`SteamController`, `SteamInventory`, `SteamParties`, `SteamMusic`, all `SteamGameServer*`. No `CreateLobby`/`JoinLobby`/`SendP2PPacket`/`ConnectP2P` flat functions appear in the dump's `NativeMethods`.

### BUT — the native DLL exposes the full modern API
`CSteamAPIContext` (line 380900) retains the interface-pointer slots for the networking stack even though the managed wrappers were stripped:
```
m_pSteamMatchmaking          // 0x20
m_pSteamMatchmakingServers   // 0x38
m_pSteamNetworking           // 0x40  (legacy ISteamNetworking P2P)
m_pSteamNetworkingUtils      // 0xC8
m_pSteamNetworkingSockets    // 0xD0  (ISteamNetworkingSockets  — GameNetworkingSockets)
m_pSteamNetworkingMessages   // 0xD8  (ISteamNetworkingMessages — connectionless P2P msgs)
m_pSteamUser                 // 0x8
```
This field layout (RemotePlay @0xC0 followed by NetworkingUtils/Sockets/Messages @0xC8/0xD0/0xD8) corresponds to **Steamworks.NET ≥ 20.1.0 / Steam SDK ≥ 1.51**. Conclusion: **the shipped `steam_api64.dll` is a modern SDK and exposes the complete flat API — `ISteamNetworkingSockets`, `ISteamNetworkingMessages`, `ISteamNetworkingUtils`, and `ISteamMatchmaking` (lobbies) are all available at runtime.**

### Transport recommendation
The mod ships its own Steamworks.NET build and P/Invokes `steam_api64.dll` directly. Both transports are viable:
- **`SteamNetworkingSockets` (ConnectP2P / CreateListenSocketP2P)** — reliable, connection-oriented, modern. Preferred for co-op state sync.
- **`SteamNetworkingMessages`** — connectionless, simpler for small frequent messages.
- **`SteamMatchmaking` lobbies** — for discovery/invites (lobby → exchange `CSteamID` → open P2P socket). `SteamFriends` (present) can drive "join game" / rich presence.

---

## 3. Orders / Jobs model + accept/complete hooks

### Data model
`Job` — TypeDefIndex 8918 (line 444808). Plain serializable class, one in-progress order:
```
int id;                      // 0x10   unique order id (accept/cancel key)
int carLoaderID;             // 0x14   which car slot
int forXP;                   // 0x18
string carFile;              // 0x20
int configVersion;           // 0x28
float timeToEnd;             // 0x2C
PaintType PaintType; Color carColor; ... Mileage; oilLevel;
bool[] jobType;              // 0x60
JobTask[] jobTasks;          // 0x68   the task list
int jobPartsCount;           // 0x70
float globalCondition;       // 0x74
bool BonusToExp;             // 0x7C
bool BonusToMoney;           // 0x7D
string LocalizationID;       // 0x80
bool IsMission;              // 0x88
int MissionID;               // 0x8C
bool CanDelete;              // 0x90
int TaskBonus;               // 0x94
int JobBonus;                // 0x98
int TotalPayout;             // 0x9C   <-- money paid on completion
int MoneySpent;              // 0xA0
int MoneySpentWithDifficultyMod; // 0xA4
int XP;                      // 0xA8   <-- xp paid on completion
bool IsCompleted;            // 0xAC
... IconType* flags ...
```
`JobTask` (8919, line 444891): `string type; string subtype; int partsCount; string desc; bool easyMode; int moneySpent; List<JobPart> Parts; bool Done;`
`JobPart` struct (8920): `string ID; string Name; bool Done; bool Found;`

### Generation / lifecycle — `OrderGenerator : MonoBehaviour` (8771, line 438377)
```
public List<Job> Jobs { get; }                        // get_Jobs @ 0x436730
private List<Job> jobs;        // 0x28   all available orders
private List<Job> selectedJobs;// 0x30   accepted/active orders
private int LastUId;           // 0x38

private void GenerateNewJob()                          // RVA 0xC5BB40
public  void AddJob(Job job, List<string[]> jobSubtypes)        // RVA 0xC5D020
public  IEnumerator TakeJob(int id, bool movePlayerToCar=true)  // RVA 0xC5D5D0  <-- ACCEPT (normal order)
private void PrepareJob(CarLoader carLoader, Job job)           // RVA 0xC5D680
public  void CancelJob(int id)                                  // RVA 0xC5F9F0  <-- DECLINE/CANCEL
public  IEnumerator TakeMission(int id, bool movePlayerToCar=true) // RVA 0xC5FD40 <-- ACCEPT (story mission)
public  void GenerateMission(int id, bool forTutorial=false)    // RVA 0xC5FDF0
public  Job GetJobForCarLoader(int carLoaderID)                 // RVA 0xC61E90
public  void Load()                                             // RVA 0xC64250
public  void Save()                                             // RVA 0xC65460
public  Action OnTakeMission;  // 0x18
```

### Accept — UI entry: `OrdersWindow : Window` (9830, line 481386)
```
private Job currentJob;        // 0xD8
private void AcceptOrderAction()     // RVA 0x9E4730   <-- button handler; calls OrderGenerator.TakeJob/TakeMission
private void DeclineOrderAction()    // RVA 0x9E49F0   <-- calls CancelJob
```
`OrderItem : ListItem` (10122, line 495392) is the per-row widget; holds `public Job job; // 0x88`, `public Action<OrderItem> OnSelect;`.

### Complete + payout — the money award happens here:
`GameScript : MonoBehaviour` (8617, line 431775) is the central world controller:
```
public Action OnEndJob;                                 // 0x130
public void EndJob(Job job, CarLoader carLoader)        // RVA 0xE95830  <-- COMPLETE: pays TotalPayout + XP (inferred)
private IEnumerator EndJobCoroutine(Job job, CarLoader carLoader) // RVA 0xE959A0
public void SellCar(CarLoader carLoader, int sellPrice) // RVA 0xE95330
public void BuyCar(CarLoader carLoader, int buyPrice)   // RVA 0xE95560
```
UI side — `OrderTab : CarInfoTab` (10052, line 491546) and the mission variant drive completion:
```
private Job job;                 // 0x90
private void SubmitAction()                 // RVA 0xC69910  (submit the finished order)
private void FinishJob()                    // RVA 0xC6B410  <-- calls GameScript.EndJob (inferred)
private void FinishMission()                // RVA 0xC6B390
private void <FinishJob>g__EndJobAskWindowAction|39_0(bool wasAccepted) // RVA 0xC6D0C0 (confirm callback)
```
(An earlier tab class has the parallel pair `FinishJob` @ line 491450 RVA 0xC6... and `<FinishJob>g__EndJobAskWindowAction|23_0`.)

**Order flow:** generate (`GenerateNewJob`) → list in `OrdersWindow` → `AcceptOrderAction` → `OrderGenerator.TakeJob(id)` moves Job from `jobs`→`selectedJobs` and loads the car → player repairs → `OrderTab.FinishJob` → `GameScript.EndJob(job, carLoader)` → `GameSettings.AddPlayerMoney(job.TotalPayout)` + `GameSettings.AddPlayerExp(job.XP)` (see §4 for those mutators).

---

## 4. Shop / Economy transactions + inventory + money

### Money / XP / scraps — all in `GameSettings` (static, 8618, line 432129)
Live counters:
```
public static int PlayerMoney;      // 0x2A0
public static int PrevPlayerMoney;  // 0x2A4
public static int AddMoneyAmount;   // 0x2A8
public static int PlayerScraps;     // 0x2AC
public static int PlayerExp;        // 0x2B8
public static int PlayerLevel;      // 0x2C8
public static int Jobs;             // 0x2D4
public static bool UnlimitedMoney;  // 0xD0
```
Mutators (THE economy choke points — all static):
```
public static void AddPlayerMoney(int money)             // RVA 0xD7B580  (+/- delta)
public static void SetPlayerMoney(int money)             // RVA 0xD7B850  (absolute)
public static int  GetStartingMoney(DifficultyLevel)     // RVA 0xD7B910
public static void AddPlayerScraps(int amount)           // RVA 0xD7B930
public static void SetPlayerScraps(int scraps)           // RVA 0xD7BBA0
public static void AddPlayerExp(int exp, bool instant=false) // RVA 0xD7BCC0
public static void AddJob(int amount)                    // RVA 0xD7C430
public static bool Save()                                // RVA 0xD7C990
public static void Load()                                // RVA 0xD7CB90
```

### Part catalog vs. owned inventory vs. transient cart
- **`GameInventory : MonoBehaviour`** (8606, line 430972) — this is the **parts/asset DATABASE** (singleton `Instance`), not the player's stock. Holds `PartPropertyList` (`IDictionary<string,PartProperty>`), thumbnails, tuning data. Key: `PartProperty GetItemProperty(string gameObjectName)` @ 0x1030780. Do NOT treat its Add* methods (AddThumbnail, AddToMaterialsDictionary…) as inventory ops.
- **`Warehouse : MonoBehaviour`** (8866, line 443137) — the **player's OWNED parts store**:
  ```
  private List<List<Item>> warehouseList;            // 0x18
  private List<List<GroupItem>> warehouseGroupList;  // 0x20
  public static int amountOfUnlockedWarehouses;      // 0x0
  public void Add(Item item)        // RVA 0x91D520   <-- gain a part
  public void Add(GroupItem item)   // RVA 0x91D5A0
  public void Delete(Item item)     // RVA 0x91D620   <-- lose/consume a part
  public void Delete(GroupItem group)// RVA 0x91D870
  public bool Save()                // RVA 0x91EC60
  public bool Load()                // RVA 0x91E3C0
  ```
- **`TempInventory`** (8821, line 441605) — transient cart (`AddItem`/`RemoveItem(BaseItem)` @ 0xADCDA0 / 0xADCE90). Used during salvage/multi-select, not the persistent store.
- `Item : BaseItem, ISave` (8915, line 444685) — a concrete owned part instance (NormalID, Condition, Quality, paint, UID, …). `TestScript.AddMoney/RemoveMoney` (line 441882) is a DEV debug component — ignore.

### `IShopItem` / `PartProperty`
```
public interface IShopItem {}                            // 8913, line 444652 (marker)
public class PartProperty : IShopItem {                  // 8927, line 445092
    string ID;        // 0x10
    int    Price;     // 0x18   <-- shop price
    int    PartGroup; // 0x1C
    string ShopGroup; // 0x20
    SpecialGroup SpecialGroup; // 0x30
    int[]  DLC;  bool HaveDLC;
    float  TuningValue, BrakesValue;
    bool   IsBody, CanPaint;
    string LocalizedName, Brand, ShopName, CarID;
    bool   IsMod;
    bool   IsBonusPart()   // RVA 0xFF1F50
}
```

### Buy flow — `ShopBuyWindow : Window` (9881, line 484550)
```
private ShopType shopType;        // 0x60
private ShopBuyItemType type;     // 0x64
private string itemID;            // 0x68
private float  currentPrice;      // 0x70
private int    currentAmount;     // 0x74
...
public  void PrepareForItem(ShopItem item, ShopType shopType) // RVA 0xA44960
private void FillItemInfo(ShopItem item)                      // RVA 0xA44C40
private void BuyItem()        // RVA 0xA44FB0   <-- THE PURCHASE (deducts money, adds to Warehouse) (inferred)
private void BuyAction()      // RVA 0xA45600   <-- confirm-button handler -> BuyItem
private void UpdatePrice()    // RVA 0xA46980
```
`ShopWindow` (9888, line 485053) and `ShopListWindow`/`ShopItem` (10066, line 492729) are navigation/listing only; the actual transaction is `ShopBuyWindow.BuyItem` → (inferred) `GameSettings.AddPlayerMoney(-currentPrice*currentAmount)` + `Warehouse.Add(new Item(...))`.

### Other economy sinks (same money API)
- `GameScript.BuyCar` / `SellCar` (RVA 0xE95560 / 0xE95330) — car trading.
- Repair mini-game window (line ~483900): `CalculateRepairPrice(Item)` @ 0x1AFF680, `RepairItem()` @ 0x1B01370, `JustTakeMyMoney()` @ 0x1B01580, `SetNoMoneyDescription()` @ 0x1B021B0 — pay-to-repair.

---

## 5. Co-op sync notes — host-authoritative actions & exact hooks

The whole economy funnels through a small set of **static** methods on `GameSettings` plus a few instance methods. Make the **host authoritative** for all state-mutating economy/order actions; clients send *requests*, host validates, applies, and broadcasts the resulting absolute state.

### Must be host-authoritative (and the methods to intercept)
| Action | Hook (method @ RVA) | Strategy |
|---|---|---|
| **Spend/earn money** | `GameSettings.AddPlayerMoney` @ 0xD7B580, `GameSettings.SetPlayerMoney` @ 0xD7B850 | Single source of truth. On client, suppress local mutation; forward intent to host. Host applies, then broadcasts `SetPlayerMoney(absolute)` to all. |
| **XP / scraps / jobs count** | `AddPlayerExp` @ 0xD7BCC0, `AddPlayerScraps` @ 0xD7B930, `SetPlayerScraps` @ 0xD7BBA0, `AddJob` @ 0xD7C430 | Same pattern — host-only apply + broadcast. |
| **Buy a part** | `ShopBuyWindow.BuyItem` @ 0xA44FB0 (confirm: `BuyAction` @ 0xA45600) | Client → request {itemID, amount, options}. Host validates funds, runs the buy (money- + `Warehouse.Add`), broadcasts money + inventory delta. |
| **Gain/lose a part** | `Warehouse.Add(Item)` @ 0x91D520, `Warehouse.Delete(Item)` @ 0x91D620 | Replicate as explicit item-grant/consume messages keyed by `Item` UID. Host authoritative over `warehouseList`. |
| **Accept an order** | `OrdersWindow.AcceptOrderAction` @ 0x9E4730 → `OrderGenerator.TakeJob` @ 0xC5D5D0 / `TakeMission` @ 0xC5FD40 | Host owns `OrderGenerator.jobs`/`selectedJobs`. Client accept = request; host runs TakeJob(id) and replicates the Job + car-load to clients. Order `id` (Job.id @0x10) is the sync key. |
| **Decline/cancel order** | `DeclineOrderAction` @ 0x9E49F0 → `OrderGenerator.CancelJob(id)` @ 0xC5F9F0 | Host-only; broadcast removal by id. |
| **Complete order + payout** | `OrderTab.FinishJob` @ 0xC6B410 / `FinishMission` @ 0xC6B390 → `GameScript.EndJob(Job, CarLoader)` @ 0xE95830 | The critical money event. Host runs `EndJob` (pays `Job.TotalPayout`/`XP`), broadcasts completion + new money/XP. Prevent double-payout: gate on `Job.IsCompleted` (@0xAC). |
| **Buy/sell car** | `GameScript.BuyCar` @ 0xE95560, `SellCar` @ 0xE95330 | Host-authoritative car slot ownership + money. |
| **Repair (pay)** | repair window `RepairItem` @ 0x1B01370 / `JustTakeMyMoney` @ 0x1B01580 | Host validates + applies condition change and money. |

### Order-generation determinism
`OrderGenerator.GenerateNewJob` @ 0xC5BB40 uses RNG + player level. Do NOT let each peer generate independently — **host generates, replicates the Job list**. `OrderGenerator.Save`/`Load` (@0xC65460/0xC64250) and `GameSettings.Save`/`Load` (@0xD7C990/0xD7CB90) define the serialized order/economy state; the host's copy is canonical, and a joining client should receive a full snapshot (money, scraps, xp, level, jobs, selectedJobs, warehouse) on connect.

### Steam/session wiring
- Gate all mod networking on `SteamManager.Initialized == true` (get_Initialized @ 0xD9C0E0). Steam is already up (AppID 1190000); do not re-`SteamAPI.Init`.
- Use the mod's own Steamworks.NET build: `SteamMatchmaking` lobby for discovery → exchange host `CSteamID` → `SteamNetworkingSockets.ConnectP2P` / `CreateListenSocketP2P` for the data channel (all supported by the shipped native `steam_api64.dll`, §2).
- Callbacks: the game already calls `SteamAPI.RunCallbacks()` in `SteamManager.Update`; the mod's `Callback<T>` registrations dispatch through the same global `CallbackDispatcher`, so lobby/connection callbacks will fire without a second pump — but keep the mod's registration lifetimes alive.
