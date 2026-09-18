using System.Runtime.CompilerServices;
using FrooxEngine;

namespace ProximityGrab;

internal enum GrabGestureKind
{
    None,
    Fist,
    Pinch,
}

internal sealed class ProximityGrabState
{
    public bool GestureDriven;
    public bool HasTrackingHands;
    public bool ProximityGrabActive;
    public GrabGestureKind ActiveGesture;
    public bool FistEngaged;
    public bool PinchEngaged;
    public Hand? GestureHand;
    public bool LastGrabResult;

    private static readonly ConditionalWeakTable<InteractionHandler, ProximityGrabState> Table = new();

    public static ProximityGrabState Get(InteractionHandler handler)
    {
        return Table.GetValue(handler, _ => new ProximityGrabState());
    }
}
