Here are my notes after researching how to implement the grab
functionality documented in `proximity-grab-bindings-spec.md`.

# Adding a SteamVR-level ProximityGrabAction action

The resonite architecture splits the game engine (FrooxEngine) and
render engine (Renderite) into two separate executables. Both
components communicate bidirectionally using IPC and shared
memory. The justification for this division, according to developers,
is that one day the renderer can be replaced.

Renderite handles polling for SteamVR actions and passes the state
over IPC to FrooxEngine. However, each action is passed over as a
boolean and the actions are hardcoded into the source code. This means
one can create a new action in the SteamVR manifest (actions.json) and
even bind them in the SteamVR Binding UI. However, because Renderite
is only listening for a fixed set of actions, it will not pick up this
new action and it will not pass it along to FrooxEngine.

This means adding a new SteamVR-level action required touching source
code at all levels of the Resonite stack:

* The actions.json manifest
* Renderite.Unity.Renderer source code (requires a rebuild)
* Renderite.Shared source code (requires a rebuild)
* FrooxEngine source code (can be handled in a mod using Harmony etc)

Such a change would need to be continuously maintained against
upstream changes.

Conclusion: A ProximityGrabAction requires a custom resonite
distributable. Feasible for a customized setup but not for
distributing to regular users.

# Alternative Hack

A self-contained mod that can be dropped into any existing
installation should still be possible, with some work on the user's
end.

A mod can be created to detect a fist gesture using the avatar's
finger skeleton in FrooxEngine and made to trigger the
GrabWithoutLaser code path. Furthermore, a thumb-and-finger precision
grab can also be detected within the mod and used to trigger the
existing precision grab code path.

The trouble is that, by default, SteamVR's index finger pinch and fist
gestures are bound to their own actions and will likely intefere with
actions triggered by the mod. So the caveat with this route is that
the user will need to edit their SteamVR bindings and unbind the index
finger pinch and fist gestures whose default bindings are
ActionPrimary and GrabAction, respectively.

# Long Term Solution

The long term solution would be to have Resonite support the following
additional SteamVR-level actions and corresponding code paths in
FrooxEngine:

* ProximityGrabAction
* LaserGrabAction
* PrecisionProximityGrabAction

The existing default SteamVR bindings would be unchanged but the above
actions would allow users fine-grained control over how they interact
with world.
