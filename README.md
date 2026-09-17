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

### Configuration

The config variables are currently hardcoded into `Mod.cs`. Edit and rebuild to change.

TODO: add GUI-based configuration in the Resonite dashboard.

### Behavior matrix

| State | Grab source | Result |
|---|---|---|
| No tracked hands | any | stock grab (laser + proximity) |
| VR controller grip | controller | stock grab (laser + proximity) |
| Hands tracked | gamepad/keyboard | stock grab (laser + proximity) |
| Hands tracked | fist gesture | proximity-only grab at the hand |
| Hands tracked | index-thumb pinch gesture | precision grab at the pinch point |
