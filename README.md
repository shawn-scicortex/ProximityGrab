# ProximityGrab

A [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader) mod that makes
grabbing require a deliberate gesture while hand tracking is live, and confines those grabs near the
hand instead of the laser.

### Install

Build and copy to the game's `rml_mods` folder:

```powershell
dotnet build ProximityGrab\ProximityGrabCustomGesture\ProximityGrabCustomGesture.csproj -p:CopyToMods=true
```

Uninstalling the mod restores stock grab behavior completely.

### Disable SteamVR Gesture Bindings

To eliminate conflicting or duplicate actions, make sure you do not have any bindings in SteamVR for the pinch and fist gestures. This mod detects its own index-thumb pinch and fist gestures.

### Configuration

The config variables are registered with RML and can be interactively changed using a config mod like [RosoniteModSettings](https://github.com/badhaloninja/ResoniteModSettings).

Additionally the mod can be turned on/off from the Grabbing context menu. This is useful if you switch to or from controllers in the game.

### Behavior matrix

| State | Grab source | Result |
|---|---|---|
| No tracked hands | any | stock grab (laser + proximity) |
| VR controller grip | controller | stock grab (laser + proximity) |
| Hands tracked | gamepad/keyboard | stock grab (laser + proximity) |
| Hands tracked | fist gesture | proximity-only grab using grab sphere |
| Hands tracked | index-thumb pinch gesture | precision grab at the pinch point |
