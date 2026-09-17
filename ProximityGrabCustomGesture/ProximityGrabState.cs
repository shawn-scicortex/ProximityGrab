using System.Runtime.CompilerServices;
using FrooxEngine;

namespace ProximityGrabCustomGesture;

internal enum GrabGestureKind
{
    None,
    Fist,
    Pinch,
}

internal sealed class ProximityGrabState
{
    public bool ProximityGrabActive;
    public bool GestureDriven;
    public bool HasTrackingHands;
    public bool Engaged;
    public bool PinchEngaged;
    public GrabGestureKind ActiveGesture;
    public Hand? GestureHand;

    private static readonly ConditionalWeakTable<InteractionHandler, ProximityGrabState> Table = new();

    public static ProximityGrabState Get(InteractionHandler handler)
    {
        return Table.GetValue(handler, _ => new ProximityGrabState());
    }
}