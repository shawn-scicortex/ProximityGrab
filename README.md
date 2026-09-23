# ProximityGrab

A [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader) mod that makes
grabbing require a deliberate gesture while hand tracking is live, and confines those grabs near the
hand instead of the laser.

### Install

1. Install [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader)

2. Place [ProximityGrab.dll](https://github.com/shawn-scicortex/ProximityGrab/releases/latest/download/ProximityGrab.dll) into your `rml_mods` folder. By default on Windows, this is `C:\Program Files (x86)\Steam\steamapps\common\Resonite\rml_mods\`

3. Start the game.

4. Enable hand tracking and you should see debug text and grab spheres around your hands and fingers. This can be turned off in the config (see below).

Uninstalling the mod restores stock grab behavior completely.

### Building

Build and copy to the game's `rml_mods` folder:

```powershell
dotnet build ProximityGrab\ProximityGrab\ProximityGrab.csproj -p:CopyToMods=true
```

### Disable SteamVR Gesture Bindings

To eliminate conflicting or duplicate actions, make sure you do not have any bindings in SteamVR for the index/middle/ring-thumb pinch and fist gestures. This mod detects its own index/middle/ring-thumb pinches and fist gestures.

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
