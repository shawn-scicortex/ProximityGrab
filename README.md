# ProximityGrab

A [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader) mod family that makes
grabbing require a deliberate **full fist** while hand tracking is live, and forces those grabs to be
**proximity-only** (never the laser).

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
  SteamVR hand skeleton (`Hand` devices / `FingerSegment` positions) and computes a geometric curl
  value for the four fingers of each hand. `PinchStrength`/`Confidence` are **not** used — those are
  only populated by the Leap Motion driver.
- **Full fist** (average curl above the engage threshold on either hand) drives the engine's own grab
  state machine (`StartGrab` / `HoldGrab` / `EndGrab`).
- The patched `InteractionHandler.Grab()` now behaves as:
  - **Hands tracked, no fist** → grab attempt is suppressed entirely (`Grab()` returns false).
  - **Full fist** → forces `Grab(laserGrab: false)`, the pure grab-sphere path.
  - **No tracked hands** (desktop / controllers-only) → 100% stock behavior.
- A postfix on `InputInterface.Bind(InputGroup)` strips the local user's built-in `Grab` binding
  (`InteractionHandlerInputs.Grab.ClearBindings()`) whenever a tracked hand is present, so the grab
  **command** can never fire independently of the fist detector. The detector calls
  `InputBindingManager.InvalidateBindings()` when tracked hands appear/disappear so the strip is
  installed and removed correctly. Remote users are never affected (`InputGroup.Owner` check).
- Sticky-grab toggle cannot engage (the grab command is unbound while hands are tracked).
  Grab is hold-to-hold.

### Install

Build and copy to the game's `rml_mods` folder:

```powershell
dotnet build ProximityGrab\ProximityGrabCustomGesture\ProximityGrabCustomGesture.csproj -p:CopyToMods=true
```

Uninstalling the mod restores stock grab behavior completely.

### Configuration

None. The design is hardcoded:

- Gesture: **full fist** only (index + middle + ring + pinky curled). Thumb is ignored.
- Engage threshold: `FistGesture.ENGAGE_DEGREES` = 150° average curl; release hysteresis:
  `RELEASE_DEGREES` = 100° (fist must open clearly before grabbing stops).
- These constants may need a tuning pass for your tracking hardware.

### Behavior matrix

| State | Grab command | Result |
|---|---|---|
| No tracked hands | bound normally | stock (laser + proximity as normal) |
| Hands tracked, no fist | stripped | nothing can grab |
| Full fist held | stripped | proximity-only grab at the hand |

Note: while a tracking hand is live, keyboard/gamepad/any grab for the local user is suppressed too —
that's intentional ("suppress entirely"). Desktop/keyboard grabbing still works when no hands are
tracked.

### HandGrabType

Respected by the forced `Grab(false)` path: off = disabled, precision / palm / auto behave as the
built-in command does.

### Build

```powershell
dotnet build ProximityGrab\ProximityGrabCustomGesture\ProximityGrabCustomGesture.csproj
```

`ResonitePath` falls back through: local `Resonite/` → `%USERPROFILE%\src\delphium_grab\` →
`%USERPROFILE%\src\delphium\` → Steam install. Override with `-p:ResonitePath=...`.