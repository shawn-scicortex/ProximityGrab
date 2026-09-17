# ProximityGrab

A [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader) mod family that makes
grabbing require a deliberate gesture while hand tracking is live, and confines those grabs near the
hand instead of the laser:

## Projects

| Project | Status | Path |
|---|---|---|
| `ProximityGrabCustomGesture` | Active | The mod does the gesture detection: unbind grabs in SteamVR, recreate the fist gesture in the mod, drive `InteractionHandler.Grab(false)` itself. |
| `ProximityGrabSteamVRAction` | Parked | One day, a SteamVR-side `ActionProximityGrab` bound in the bindings UI. Blocked: the renderer must poll the new action and IPC it through `Renderite.Shared`'s controller state before any game-side mod can see it. |

## ProximityGrabCustomGesture

Purpose: hand-tracking false positives (a pinch or partial curl reporting the grip) can make Resonite
laser-grab objects from across the room. This mod gates grabbing behind an actual fist gesture and,
even when the fist is held, restricts grab execution to objects physically touching the hand's grab
area (`Grab(laserGrab: false)` — `TryPointGrab()` never runs).

### How it works

- A per-frame detector (patched onto `InteractionHandler.OnCommonUpdate`, local user only) reads the
  tracked hand skeleton (`Hand` devices / `FingerSegment` positions) and computes gestures per hand.
  `PinchStrength`/`Confidence` are **not** used — those are only populated by the Leap Motion driver.
- Two gestures, one per hand:
  - **Full fist** — average curl of index/middle/ring/pinky above the engage threshold, **and** each
    finger individually above its own minimum (a pointing/one-finger-straight hand no longer counts).
  - **Pinch** — index tip and thumb tip closer than the pinch distance, index not folded, and the
    full fist *not* engaged. When both latch on the same hand, **fist wins**.
- A detected gesture drives the engine's grab state machine (`StartGrab` / `HoldGrab` / `EndGrab`):
  - **Fist** → forces `Grab(laserGrab: false)`, the pure grab-sphere path.
  - **Pinch** → a self-contained precision grab: sphere overlaps swept outward from the point midway
    between the tracked index and thumb tips (radii 0.005→0.05 scaled by user scale), grabbing a
    single grabbable (`Grabber.Grab(..., singleItem: true)`). Mirrors the engine's `PrecisionGrab`
    algorithm but uses the live `Hand` skeleton instead of a `HandPoser` component, so it works on
    any avatar. Gestures that grab the same object as a normal palm grab behave the same.
- The patched `InteractionHandler.Grab()` now behaves as:
  - **Hands tracked + gesture** → gesture-driven grab above (fist = proximity, pinch = precision).
  - **Hands tracked, no gesture** → the grab *command* from gamepad/keyboard runs stock default
    (laser grab at the pointer when active, else grab sphere). Wired grip binds from VR controllers
    are stripped, but since hand tracking and controllers can't be used at once this is belt-and-braces.
  - **No tracked hands** (desktop / controllers-only) → 100% stock behavior.
- A postfix on `InputInterface.Bind(InputGroup)` removes only the local user's VR-controller `Grab`
  bindings (`ImplicitDevice is ControllerBase`); gamepad and keyboard/mouse grab bindings are kept.
  The detector calls `InputBindingManager.InvalidateBindings()` when tracked hands appear/disappear so
  the strip is installed and removed correctly. Remote users are never affected (`InputGroup.Owner` check).
- Sticky-grab toggle cannot engage from a controller while hands are tracked.

### Install

Build and copy to the game's `rml_mods` folder:

```powershell
dotnet build ProximityGrab\ProximityGrabCustomGesture\ProximityGrabCustomGesture.csproj -p:CopyToMods=true
```

Uninstalling the mod restores stock grab behavior completely.

### Configuration

None. The design is hardcoded on `ProximityGrabCustomGestureMod` in `Mod.cs`; edit + rebuild to change:

- Gesture toggles: `ProximityGrabCustomGestureMod.FistGrabEnabled`,
  `ProximityGrabCustomGestureMod.PrecisionGrabEnabled`.
- **Full fist** (index + middle + ring + pinky curled) and **pinch** (index tip + thumb tip
  close together). Fist wins over pinch on the same hand; a pinch is ignored while the fist is engaged.
- Fist thresholds: `FistEngageDegrees` = 150° average curl; release hysteresis
  `FistReleaseDegrees` = 100° (fist must open clearly before grabbing stops). Per-finger minimum:
  `FistMinEngageDegrees` = 90° to engage, `FistMinReleaseDegrees` = 70° to release — every finger
  must stay curled, so a straight index or middle finger no longer grabs.
- Pinch thresholds: `PinchEngageDistance` = 0.030 and `PinchReleaseDistance` = 0.050 (scale-normalized
  units); `PinchMaxIndexCurlDegrees` = 140° stops a folded index from faking a pinch.
- Precision sphere sweep: `PrecisionMinRadius` / `PrecisionMaxRadius` / `PrecisionRadiusStep` =
  0.005 / 0.05 / 0.01 (scaled by user scale, m).
- These values may need a tuning pass for your tracking hardware.

### Behavior matrix

| State | Grab source | Result |
|---|---|---|
| No tracked hands | any | stock (laser + proximity as normal) |
| Hands tracked, no gesture | gamepad/keyboard | stock default (laser grab if pointer active, else grab sphere) |
| Hands tracked, no gesture | VR controller grip | stripped (belt-and-braces; hand tracking + controllers are mutually exclusive anyway) |
| Full fist held | gesture | proximity-only grab at the hand |
| Pinch held (fist open) | gesture | precision grab at the pinch point (single item) |

### HandGrabType

Respected by the forced `Grab(false)` fist path: off = disabled, precision / palm / auto behave as the
built-in command does. The pinch path builds its colliders itself and does not depend on `HandGrabType`.

### Build

```powershell
dotnet build ProximityGrab\ProximityGrabCustomGesture\ProximityGrabCustomGesture.csproj
```

`ResonitePath` falls back through: local `Resonite/` → `%USERPROFILE%\src\delphium_grab\` →
`%USERPROFILE%\src\delphium\` → Steam install. Override with `-p:ResonitePath=...`.