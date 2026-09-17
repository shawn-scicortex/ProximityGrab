using System.Runtime.CompilerServices;
using FrooxEngine;

namespace ProximityGrabCustomGesture;

internal sealed class ProximityGrabState
{
    public bool ProximityGrabActive;
    public bool HasTrackingHands;
    public bool Engaged;

    private static readonly ConditionalWeakTable<InteractionHandler, ProximityGrabState> Table = new();

    public static ProximityGrabState Get(InteractionHandler handler)
    {
        return Table.GetValue(handler, _ => new ProximityGrabState());
    }
}