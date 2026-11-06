# CMS2021 RE — Player, Camera, Interaction & Tools

Scope: player controller, camera, interaction/raycasting, tools, hands. Source: `re/output/dump.cs`
(Il2CppDumper, Unity 2020.3.49f1 IL2CPP). All offsets are field offsets within the managed object;
RVA/Offset are the x64 image addresses of methods (hook targets).

---

## 0. TL;DR architecture

The player is a **disembodied first-person rig**: a `CharacterController` capsule driven by
`CharacterMotor` (position) + `FPSCamera` (yaw/pitch look). **There is NO visible body or hands mesh**
— to show player 2 we must spawn our own proxy.

Per-frame pointing/interaction is centralized:

```
FPSCamera.UpdateInput()  -> m_Yaw / m_Pitch         (look direction)
CharacterMotor           -> transform.position        (where the player stands)
Raycast.Update()         -> dispatch by gameMode -> Garage()/GarageAssemble()/PartSelect()/...
    raycasts from mainCamera, finds InteractiveObject / PartScript / MountObject under cursor
    -> GameScript.SetIOMouseOver(...) / SetPartMouseOver(...)   (hover + highlight)
    -> on click: GameScript.ClickIO(int no)                     (THE action entry point)
GameMode.Get().currentMode : gameMode                 (what the player is currently doing)
```

The two `PlayerController` types the brief mentions (TypeDefIndex **5002** `ComponentWrapper<PlayerController>`
and **5142** `IPlayerController`, plus `PlayerMouse` 5006/5146) are **Rewired input-library** classes, NOT
the game avatar. Ignore them for presence; they only matter if we hook raw input.

---

## 1. Player & camera

### 1.1 Movement — `CharacterMotor : MonoBehaviour`  (TypeDefIndex 8566, line 429591)
The actual kinematic controller. Attached to the player root GameObject together with Unity
`CharacterController`.

```
bool    canControl;          // 0x18
bool    useFixedUpdate;      // 0x19
Vector3 inputMoveDirection;  // 0x1C   <- desired move dir (set by FPSInputController)
bool    inputJump;           // 0x28
CharacterMotorMovement movement;        // 0x30  (contains velocity/frameVelocity)
CharacterMotorJumping  jumping;         // 0x38
CharacterMotorMovingPlatform movingPlatform; // 0x40
CharacterMotorSliding  sliding;         // 0x48
bool    grounded;            // 0x50
Vector3 groundNormal;        // 0x54
GroundType groundType;       // 0x60
Transform tr;                // 0x68    <- the player transform (position+rotation source)
CharacterController controller; // 0x70
```
`CharacterMotor.CharacterMotorMovement` (8560, line 429456): `Vector3 velocity //0x3C`,
`Vector3 frameVelocity //0x48`, max*Speed, gravity, etc.
Key methods: `UpdateFunction()` 0xF867E0, `FixedUpdate()` 0xF87E80, `SetVelocity(Vector3)` 0xF89F10,
`SetControllable(bool)` 0x479050.

**Player world position = `CharacterMotor.tr.position`** (= the player root transform).

### 1.2 Input glue — `FPSInputController : MonoBehaviour` (TypeDefIndex 8591, line 430525)
Reads InputManager axes each `Update()` and feeds `motor.inputMoveDirection`.
```
bool       IsReady;          // 0x18
CharacterMotor motor;        // 0x20
float      CONST_MAX_LENGTH; // 0x28
float      speedAdd;         // 0x2C
InputManager InputManager;   // 0x30
Vector3    originalPosition; // 0x38
Camera     mainCamera;       // 0x48
bool       running;          // 0x50
```
Methods (hooks for teleport/placement):
- `void Update()` 0xBFDD10 — reads input, drives motor.
- `void SetCharacterControllerPosition(bool setOriginal=false)` 0xBFE2C0
- `void SetPositionNextToCar(CarLoader)` 0xBFE880
- `Vector3 GetCameraRotationToCar(CarLoader)` 0xBFF1E0
- `void ResetPosition()` 0xBFF5D0 ; `void UpdateSpeed()` 0xBFF690
A reference is cached on the global singleton: `GameManager.FPSInputController` (field 0x70).

### 1.3 Look / FP camera — `FPSCamera : MonoBehaviour` (TypeDefIndex 8452, line 423479)
The authoritative first-person camera. **This is where look direction lives.**
```
float   HeadHeight;          // 0x18
bool    readInput;           // 0x1C   <- gate: is camera accepting mouse-look
CharacterController FPSCharacterController; // 0x48
CharacterMotor      FPSCharacterMotor;      // 0x50
Camera  MainCamera;          // 0x58
Vector2 mouseLookSensitivity;// 0x60
...
float   m_Yaw;               // 0x9C   <- horizontal look (body yaw)
float   m_Pitch;             // 0xA0   <- vertical look (camera pitch)
float   m_Roll;              // 0xA4
Vector2 RotationYawLimit;    // 0xB0
Vector2 RotationPitchLimit;  // 0xB8
InputManager InputManager;   // 0x110
bool    IsReady;             // 0x118
```
Methods:
- `void Update()` 0xBF8FD0 ; `void UpdateInput()` 0xBFAF70 — applies mouse-look to m_Yaw/m_Pitch.
- `void SetRotationToCharacter()` 0xBFB350 — pushes yaw onto the character body.
- `Vector2 GetCameraLook()` 0xBFB870 ; `void UpdateRotation(float)` 0xBFA100
- `void SetSavedRotation(Vector3)` 0xBFB840 — **apply-side hook** to force look dir.
- `void LookAt(CarLoader)` 0xBFB6D0 ; `void EnablePhotoMode(bool,bool)` 0xBF9B90 ; `void LoadData()` 0xBFC0D0
Mouse-look tuning is stored globally in `GameSettings`: `inputFPS_MouseLookSensitivity` (0x9C) etc.

**Look direction to replicate = `FPSCamera.m_Yaw` + `FPSCamera.m_Pitch`** (cheaper/cleaner than the
camera quaternion; roll is ~0).

### 1.4 Camera mode managers
- `CameraManager : MonoBehaviour` (8464, line 424178, singleton via `Get()` 0xF50090):
  `CameraState CurrentCameraState //0x28`, `PreviousCameraState //0x2C`, `GameObject playerGameObject //0x30`,
  `OrbitCamera //0x38`, `FixedCamera //0x40`. `ChangeCamera(CameraState)` 0xF4F1D0,
  private `EnableFPSCamera(bool)` 0xF4F800 / `EnableOrbitCamera` / `EnableFixedCamera`,
  `RefreshFPSCameraSettings()` 0xF4FF70.
- `enum CameraState` (9002): **FPS=0**, Orbit=1, Fixed=2, UI=3. Co-op presence only matters while a player
  is in the FPS (walk-around) state.
- `CameraController : MonoBehaviour` (8463, line 424133): thin wrapper holding a `VPCameraController vpCamera`
  for the car/orbit view; `LookAtOpponent()` exists but is PvP race cam, not co-op.
- `VPCameraController` (9228, line 455631) and `FieldOfViewController` (9182) are **Vehicle-Physics camera**
  rig (orbit/smooth-follow/free for driving a car) — not the on-foot player. Skip for presence.

### 1.5 Body / hands model — DOES NOT EXIST
No `PlayerBody`, `Hands`, `Arms`, `FirstPersonArms`, `Mechanic`, `PlayerAvatar` class exists. The only
`Avatar` (3936) is Unity's animation avatar; `CharacterController` (4011) is Unity's. The player is a
capsule + camera with head-bob only (`FPSCamera.bobbingSpeed/bobbingAmount`). Tools are shown via a
separate render-camera overlay (`ToolsManager.CarToolsRenderCamera`, `cameraRawImage`), not held in a
visible hand.

**Implication for co-op:** to show player 2 we must **spawn our own proxy** — e.g. a capsule or an
imported humanoid — parented to a networked transform, and orient its head/torso from the replicated
`m_Yaw`/`m_Pitch`. Nothing in the game needs to be "unhidden"; there is simply no avatar asset.

---

## 2. Interaction system (raycast / hover / use)

### 2.1 `Raycast : MonoBehaviour` (TypeDefIndex 8795, line 440700)
The per-frame cursor raycaster. One instance; enabled/disabled by `GameMode.EnableRaycast`.
```
Camera mainCamera;   // 0x18
Ray    ray;          // 0x20
RaycastHit hit;      // 0x38
Transform prevTransform; // 0x68
LayerMask garageRaycastMask / garageRaycastMaskTutorial / garageAssembleRaycastMask /
          bonusAssembleRaycastMask / interiorDisassembleRaycastMask / carColliderMask /
          examineGarageRaycastMask / examineRaycastMask / partSelectRaycastMask /
          partSelectMountRaycastMask / mountObjectsRaycastMask;   // 0x70..0x98
PartScript        pS;      // 0xA0   <- part currently hit
InteractiveObject iO;      // 0xA8   <- IO currently hit
InteractiveObject prevIO;  // 0xB0
MountObject       PrevMO;  // 0xB8   <- bolt currently hit
GameScript        gameScript; // 0xC8
InputManager      inputManager; // 0xF8
```
Consts: `CONST_IO_DISTANCE = 3.5`, `garageRaycastLength = 7`, spherecast size 0.01 / dist 3.4.
`Update()` 0x992A40 dispatches to one handler per `gameMode`:
`Garage()` 0x992C00, `ExamineGarage()` 0x9942C0, `InteriorDisassemble()` 0x995170,
`InteriorAssemble()` 0x995F60, `GarageAssemble()` 0x996CF0, `PartSelect()` 0x997530,
`DrainTool()` 0x997F60, `ExamineCondition()` 0x998950, `PartSelectMount()` 0x999310,
`PartUnMountPartMount()` 0x999DE0, `BonusDisassemble()` 0x99AEC0, `BonusAssemble()` 0x99C3E0.
These call back into `GameScript` to set hover state and, on click, to act.

### 2.2 `GameScript : MonoBehaviour` (TypeDefIndex 8617, line 431775) — the master gameplay controller
Singleton via `GameScript.Get()` 0xE86260. Holds the hover state and all action entry points.
```
SceneType CurrentSceneType;     // 0x18   <- which level (Garage=4)
ToolType  currentExamineType;   // 0x1C
Raycast   raycast;              // 0x38
PartScript partMouseOver;       // 0x50   <- part under cursor now
PartScript focusedPartScript;   // 0x58
BaseItem  SelectedToMount;      // 0x60
GameMode  GameMode;             // 0x70
InputManager InputManager;      // 0x78
string    IOMouseOverType;      // 0x98
GameObject IOMouseOverGO;       // 0xA0
InteractiveObject IOMouseOverIO;// 0xA8   <- IO under cursor now
CarLoader IOMouseOverCarLoader; // 0xB0
CarPart   IOMouseOverCarPart;   // 0xB8
string    raycastOnItemName;    // 0xE0
string    raycastOnItemID;      // 0xE8
PartScript SelectedPart;        // 0x128
Action    OnEndJob;             // 0x130
```

**Hover (set every frame by Raycast):**
- `void SetIOMouseOver(GameObject go, string type, InteractiveObject io)` 0xE87270
- `void SetIOMouseOverNull()` 0xE890E0
- `void SetPartMouseOver(PartScript part)` 0xE8CB30
- `void UpdateRaycastOnItemName()` 0xE8BE40 / `UpdateRaycastOnItemID()` 0xE8C0C0

**>>> PRIMARY ACTION HOOK <<<**
- `void ClickIO(int no)` **0xE894F0** — invoked when the player clicks on whatever is hovered.
  `no` = mouse button / action index. This is THE single choke point for "player used/clicked a thing"
  and the best place to broadcast an interaction event (it has access to IOMouseOverIO, partMouseOver,
  IOMouseOverCarPart, current gameMode). Local functions `<ClickIO>g__RunPathTest|0` (line 431439) and
  `<ClickIO>g__RunDyno|72_1` 0xE95B70 confirm it routes mode-specific sub-actions.
- `void WorkOnIO()` 0xE8D880 — runs the "work on the hovered IO" (tool animation / progress) after a click.

**Mount / unmount entry points (part-level):**
- `void SelectToMount(PartScript target, MountObject[] mountObjects)` 0xE91EB0
- `void SelectToUnMount(PartScript target, MountObject[] mountObjects)` 0xE92390
- `void PrepareItemsToMount()` 0xE93590 / `PrepareItemToMount()` 0xE93AD0
- `void SelectPartToMount(BaseItem)` 0xE8F530 ; `void ShowSelectPartMount(string id)` 0xE8F3F0

**Per-frame input:** `void Update()` 0xE8FC00 -> `bool HandlePlayerInput()` 0xE90070
(+ `HandleGameModeChange` 0xE913B0, `HandleWindowOpening/Closing`, `HandleActiveDescriptionsInput`).

### 2.3 `InteractiveObject : MonoBehaviour` (TypeDefIndex 8671, line 434659) — a mountable/usable object
Represents a garage machine or a removable assembly. Highlights on hover, gates mount/unmount.
```
string id;            // 0x18
bool   on;            // 0x20
string type;          // 0x28
IOSpecialType specialType; // 0x30
Highlighter ho;       // 0x38
bool   mouseOver;     // 0x40
float  distanceMod;   // 0x44
List<CarPart> blockedBy / unblockedBy; // 0x78 / 0x80
Action OnTakeOffFinished / OnTakeOnStarted / OnTakeOnFinished; // 0x98 / 0xA0 / 0xA8
```
Methods: `SetMouseOver(bool, Color)` 0xC72290 / `SetMouseOver(bool)` 0xC722A0, `On()` 0xC723A0,
`Off()` 0xC72350, `bool CanMount()` 0xC70440, `bool CanUnmount()` 0xC70790, `bool CanHold()` 0xC708C0,
`HighlightAll(Color)` 0xC72AC0, `HighlightNone()` 0xC72B70.
The scene-level machine IOs are wired on the game `SceneManager` (see §5): `GarageLook`, `Toolbox`,
`EngineCrane`, `Welder`, `Computer`, `Phone`, `Radio`, `BrakeLathe`, `InteriorDetailingToolkit`, etc.

### 2.4 `PartScript : MonoBehaviour` (TypeDefIndex 8779, line 438736) — a single car part
The unit that gets removed/installed/painted/examined. Carries replicable part state.
```
bool   On;            // 0x1A
string id;            // 0x20
string tunedID;       // 0x28
bool   IsUnmounted;   // 0x38
float  Condition;     // 0x3C
int    Quality;       // 0x40
bool   IsExamined;    // 0x44
bool   MountAnimationCompleted; // 0x45
float  Dust;          // 0x48
MountObject[] MountObjects; // 0x50   <- the bolts/screws fastening this part
direction unmountDirection; // 0xB8
bool   MouseOver;     // 0x100
bool   mountMode;     // 0x101
Color  currentColor;  // 0x190
bool   IsPainted;     // 0x1A0
PaintType CurrentPaintType; // 0x1A1
Action OnMountFinished / OnSelectToUnMount / OnHidePart / OnShow; // 0x1D0 / 0x1D8 / 0x1C0 / 0x1E0
```
Methods: `SetMouseOver(bool)` 0xFF1FA0, `bool IsBlocked()` 0xFF1F90, `GetID()` 0x436720,
`SetColor(Color)` 0xFF2790, `GetCanUnmountSingle()` 0xFF1FD0.
Its `OnMountFinished` / `OnSelectToUnMount` Actions are convenient **state-change broadcast hooks**.

### 2.5 `MountObject : MonoBehaviour` (TypeDefIndex 8711, line 436583) — a bolt / screw / fastener
This is the thing you point at and "use the tool on" to unscrew. The tool used is implicit (the game
picks the right wrench/ratchet/screwdriver animation automatically — there is no equip step for these).
```
bool    canBeUnmount; // 0x18
float   mountState;   // 0x1C   <- 0..1 progress of unscrewing
float   Condition;    // 0x20
bool    unmounted;    // 0x90
bool    mouseOver;    // 0x92
bool    canAction;    // 0x93
bool    IsStuck;      // 0x94
Transform child;      // 0x98
```
**>>> PER-BOLT ACTION HOOK <<<**
- `void Action()` **0x9C6920** — performs one unscrew/remove step on this bolt (the literal "use tool
  on bolt" call). Hook to replicate individual bolt removal.
- `void UpdateCondition(float)` 0x9C5160, `void SetMouseOver(bool)` 0x9C5500,
  `bool IsUnmounted()` 0x9C58B0, `void SetFullUnmountPosition()` 0x9C58F0,
  `void SetFullMountPosition()` 0x9C5B80, `void PrepareToMount()` 0x9C6CC0, `void Update()` 0x9C75D0.

### 2.6 `GameMode : MonoBehaviour` (TypeDefIndex 8607, line 431351) — current action/mode
Singleton via `Get()` 0x103E580. The single best "what is the player doing" signal.
```
Action OnPartSelectMode;     // 0x18
Action<gameMode> OnModeChange; // 0x20   <- broadcast hook for mode changes
gameMode currentMode;        // 0x30
gameMode previousMode;       // 0x31
bool mountUnMountMode;       // 0x32
bool BlockModeChange;        // 0x33
```
Methods: `gameMode GetCurrentMode()` 0x69D7C0, `void SetCurrentMode(gameMode)` 0x103B500 (apply-side hook),
`bool CompareWithCurrentMode(gameMode)` 0x103AD20, `void SetPartSelectMode(Transform)` 0x103E0E0,
`void EnableRaycast(bool)` 0x103E470.
`enum gameMode` (8992, **byte**): Garage=0, GarageDisassemble=1, GarageAssemble=2, InteriorDisassemble=3,
InteriorAssemble=4, Interior=5, PartSelect=6, UI=7, PhotoMode=8, PartMount=9, PartUnMount=10,
GroupUnMount=11, GroupMount=12, PartSelectMount=13, ExamineCondition=14, CarDrive=15, PathTest=16,
ExamineTools=17, Dyno=18, Benchmark=19, ExamineGarage=20, BonusAssemble=21, BonusDisassemble=22,
DrainTool=23, None=24.

---

## 3. Tools — how the equipped tool is tracked & switched

CMS has two tool categories:

**(a) Hand fasten/unfasten (ratchet/wrench/screwdriver):** NOT a selectable equipped item. There is no
`Screwdriver`/`Ratchet`/`Wrench` class. Pointing at a `MountObject` and clicking (`GameScript.ClickIO`
-> `MountObject.Action()`) auto-plays the correct animation. So "equipped tool" for bolts = nothing to sync
beyond the click + which bolt.

**(b) Special/station tools (OBD, multimeter, refills, drains, sprays):** tracked by
`ToolsManager : MonoBehaviour` (TypeDefIndex 8708, line 436350), singleton via `Get()` 0x10D3B50.
```
ExamineTool TireTreadDepthTester / Multimeter / Compression / ObdScanner / CompoundMeter / FluidExtractor; // 0x48..0x70
OilBayonet  OilBayonet;      // 0x78
bool        ToolIsActive;    // 0x80
FluidRefill OilRefill / BrakeRefill / CoolantRefill / WindscreenWashRefill / PowerSteeringRefill; // 0x88..0xA8
Transform   CameraParent;    // 0xB0
GameObject  CurrentUsedTool; // 0xE0   <- the active tool GameObject
GameObject  ItemWorkOn;      // 0xE8   <- target being worked on
ToolType    currentUsedTool; // 0xF0   <- the active tool enum (note: lowercase field)
CarFluidType CurrentCarFluidType; // 0xF8
Action      OnOilDrainFinished / OnHideTool; // 0x100 / 0x108
```
Methods:
- `void Use(ToolType tool)` **0x10D2A10** — **equip/activate hook** (switches the current tool).
- `void HideTool()` 0x10D37D0 / `void HideTool(ToolType)` 0x10D3880 — unequip.
- `bool IsToolActive()` 0x4BA6E0 ; `FluidExtractor GetFluidExtractor()` 0x10D2CA0.
- Coroutines `UseEngineCrane(CarLoader)` 0x10D3200, `UseOilDrain(CarLoader)` 0x10D36D0,
  `UseEngineCraneMount(CarLoader)` 0x10D3300, `ShowMountObjectSpray()` 0x10D3170.
`enum ToolType` (8982): OBD=0, TestDrive=1, PathTest=2, Compression=3, Multimeter=4,
TireTreadDepthTester=5, CompoundMeter=6, OilBayonet=7, OilRefill=8, OilDrain=9, BrakeRefill=10,
CoolantRefill=11, WindscreenWashRefill=12, PowerSteeringRefill=13, MountObjectSpray=14, FluidExtractor=15.

**Large station tools:** `GarageTool : MonoBehaviour` abstract (TypeDefIndex 9056, line 447854):
fields `effectTime //0x18`, `ParticleSystem particles //0x20`, `InteractiveObject interactiveObject //0x28`,
`string sfx //0x30`. Abstract `IEnumerator DoWorkAnim(CarLoader)` (slot 4), `void Use()` (slot 6),
`void Use(CarLoader)` (slot 7); virtual `StartAnim(CarLoader)` 0xD6C570; `FinishAnim(CarLoader)` 0xD6CC90.
Concrete subclasses incl. `CarWashLogic : GarageTool` (9044, line 447410), plus
`InteriorDetailingToolkitLogic`, and (held on SceneManager) WheelBalancerLogic, TireChangerLogic,
BatteryChargerLogic, EngineStandLogic, SpringClampLogic, BrakeLatheLogic. Hook `GarageTool.Use(CarLoader)`
(slot 7, virtual) to replicate station-tool usage.

**Equipped-tool state to sync = `ToolsManager.currentUsedTool` (ToolType, 0xF0) + `ToolIsActive` (0x80).**

---

## 4. Minimal "player presence" state + hook points

Per-frame replicated snapshot for the remote proxy:

| Field | Source (read) | Apply (write / proxy) |
|---|---|---|
| position (Vector3) | `CharacterMotor.tr.position` (motor on player root; motor reachable via `GameManager.FPSInputController.motor`) | `proxy.transform.position` |
| yaw (float) | `FPSCamera.m_Yaw` (0x9C) | rotate proxy body / `SetSavedRotation` on a proxy cam |
| pitch (float) | `FPSCamera.m_Pitch` (0xA0) | tilt proxy head |
| current action | `GameMode.Get().GetCurrentMode()` (0x30, `gameMode`) | drive proxy animation/label |
| camera state | `CameraManager.Get().CurrentCameraState` (FPS vs Orbit/Fixed/UI) | decide whether to render proxy on foot |
| hovered IO / part | `GameScript.IOMouseOverIO` (0xA8) / `partMouseOver` (0x50) / `IOMouseOverCarPart` (0xB8) | optional: show "player 2 is looking at X" |
| equipped station tool | `ToolsManager.currentUsedTool` (0xF0) + `ToolIsActive` (0x80) | show tool overlay on proxy |
| carried/selected part | `GameScript.SelectedPart` (0x128) / `SelectedToMount` (0x60) | optional |

Event (not per-frame) hooks to network discrete actions:
- **Click/use:** `GameScript.ClickIO(int no)` @0xE894F0 — broadcast {button, hovered IO/part id, gameMode}.
- **Bolt unscrew step:** `MountObject.Action()` @0x9C6920 — broadcast per-bolt progress/removal.
- **Mount/unmount part:** `GameScript.SelectToMount(PartScript, MountObject[])` @0xE91EB0 /
  `SelectToUnMount(...)` @0xE92390; or subscribe `PartScript.OnMountFinished` / `OnSelectToUnMount`.
- **Mode change:** subscribe `GameMode.OnModeChange` (Action<gameMode>, 0x20) or hook `SetCurrentMode` @0x103B500.
- **Station tool:** `ToolsManager.Use(ToolType)` @0x10D2A10 and `GarageTool.Use(CarLoader)` (vtable slot 7).
- **Hover (for the "looking at" indicator):** `GameScript.SetIOMouseOver(...)` @0xE87270 /
  `SetPartMouseOver(...)` @0xE8CB30.

Apply-side for authoritative part/bolt state on the remote: call `MountObject.SetFullUnmountPosition()` /
`SetFullMountPosition()` / `UpdateCondition(float)`, and `PartScript.SetColor` / mount via the same
`GameScript.SelectToMount`/`SelectToUnMount` path to reuse the game's animations.

**Remote-player proxy:** since no avatar mesh exists, spawn a capsule (or imported humanoid) at init when a
peer enters FPS state; each frame set its transform from replicated position+yaw, tilt a head child by
pitch, and optionally parent a copy of the active tool model (`ToolsManager.CurrentUsedTool`, 0xE0) to a
hand bone. Hide it when the peer's `CameraState != FPS` or `gameMode == UI`.

---

## 5. Garage scene detection (is the local player in garage gameplay?)

Signals, strongest first:

1. **`GameScript.CurrentSceneType` (field 0x18, `enum SceneType`)** — read via `GameScript.Get()`.
   `SceneType.Garage == 4` (enum 8980: None=0, Junkyard=1, Barn=2, Showroom=3, **Garage=4**, Auction=5,
   TestTrack=6, ... Menu=10, Tutorial=15, ...). `GameScript.Get()` returning non-null already means a
   gameplay scene is loaded (it is absent in pure menus).

2. **Game's own `SceneManager : MonoBehaviour` (TypeDefIndex 9582, line 467749)** — singleton `Get()`
   @0xBC35A0. Present only in the garage/tutorial gameplay scene; holds the whole garage rig:
   `FPSCamera FPSCamera //0x30`, `CharacterController CharacterController //0x38`,
   `Transform PlayerStartPosition //0x28`, `CarLifter //0x58`, `CarLoader //0x60`, and all machine IOs
   (`GarageLook`, `Toolbox`, `EngineCrane`, `Welder`, `Computer`, `BrakeLathe`, ...). `bool TutorialInProgress //0x18`.
   `SceneManager.Get() != null` is a solid "in garage-style gameplay scene" gate. (This is distinct from
   Unity's `SceneManager` 3579 and `SceneManagerAPI` 3578.)

3. **`GarageLevelManager : MonoBehaviour` (TypeDefIndex 8622, line 432311)** — `bool IsReady //0x18`,
   `PrepareGarage()` @0xD67FB0. Its presence + `IsReady==true` marks the garage fully prepared.
   Loaded by **`GarageLoader`** (TypeDefIndex 9097, line 449693) whose coroutines `PrepareGame`/`Load`/
   `LoadTestMode` build the garage.

4. **Scene transitions** are driven by `NotificationCenter.SelectSceneToLoad(string newSceneName,
   SceneType sceneType, bool useFader, bool saveGame)` (lines 438095–438102) and
   `GameManager : Singleton<GameManager>` (TypeDefIndex 9084, line 448947). Hook `SelectSceneToLoad`
   to learn the target `SceneType` before a load, and gate co-op activation on `sceneType == Garage`.

Recommended runtime check: `SceneManager.Get() != null && GameScript.Get()?.CurrentSceneType == SceneType.Garage`
and `GameMode.Get().GetCurrentMode() != gameMode.None`. Enable the co-op proxy/sync only then.

---

## Appendix — TypeDefIndex quick map
FPSInputController 8591 · CharacterMotor 8566 · FPSCamera 8452 · CameraController 8463 · CameraManager 8464 ·
VPCameraController 9228 · FieldOfViewController 9182 · Raycast 8795 · GameScript 8617 · GameMode 8607 ·
InteractiveObject 8671 · PartScript 8779 · MountObject 8711 · ToolsManager 8708 · ToolsMoveManager 8709 ·
GarageTool 9056 · CarWashLogic 9044 · GameManager 9084 · SceneManager(game) 9582 · GarageLevelManager 8622 ·
GarageLoader 9097 · enum gameMode 8992 · enum ToolType 8982 · enum SceneType 8980 · enum CameraState 9002.
Rewired (ignore for presence): PlayerController 5002/5142, PlayerMouse 5006/5146.
