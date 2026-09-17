using System;
using System.Collections.Generic;
using Elements.Core;
using FrooxEngine;
using Renderite.Shared;

namespace ProximityGrabCustomGesture;

internal static class FistGesture
{
    private const float ENGAGE_DEGREES = 150f;
    private const float RELEASE_DEGREES = 100f;

    private static readonly List<Hand> Devices = new();

    public static bool HasTrackingHands { get; private set; }

    public static void Update(InteractionHandler handler)
    {
        var state = ProximityGrabState.Get(handler);

        Devices.Clear();
        handler.World.InputInterface.GetDevices(Devices);

        Hand? left = null;
        Hand? right = null;
        bool anyHands = false;
        foreach (var hand in Devices)
        {
            if (!hand.IsTracking)
                continue;
            anyHands = true;
            if (hand.Chirality == Chirality.Left)
            {
                if (left == null || hand.Wrist.Priority > left.Wrist.Priority)
                    left = hand;
            }
            else if (right == null || hand.Wrist.Priority > right.Wrist.Priority)
            {
                right = hand;
            }
        }

        if (state.HasTrackingHands != anyHands)
        {
            state.HasTrackingHands = anyHands;
            handler.Input.InvalidateBindings();
        }

        bool leftEngaged = UpdateHand(left, ref state.LeftEngaged);
        bool rightEngaged = UpdateHand(right, ref state.RightEngaged);

        if (!state.HasTrackingHands)
        {
            leftEngaged = false;
            rightEngaged = false;
        }

        bool nowActive = leftEngaged || rightEngaged;

        if (state.ProximityGrabActive && !nowActive)
        {
            Release(handler, state);
        }
        else if (!state.ProximityGrabActive && nowActive)
        {
            state.ProximityGrabActive = true;
            Methods.StartGrab.Invoke(handler, null);
        }
        else if (state.ProximityGrabActive)
        {
            Methods.HoldGrab.Invoke(handler, null);
        }
    }

    private static bool UpdateHand(Hand? hand, ref bool prevEngaged)
    {
        if (!IsUsable(hand))
        {
            prevEngaged = false;
            return false;
        }
        float curlDegrees = AverageCurlDegrees(hand!);
        bool engaged = prevEngaged ? curlDegrees > RELEASE_DEGREES : curlDegrees > ENGAGE_DEGREES;
        prevEngaged = engaged;
        return engaged;
    }

    private static bool IsUsable(Hand? hand)
    {
        return hand != null
               && IsUsableFinger(hand.Index)
               && IsUsableFinger(hand.Middle)
               && IsUsableFinger(hand.Ring)
               && IsUsableFinger(hand.Pinky);
    }

    private static bool IsUsableFinger(Finger finger)
    {
        int count = 0;
        if (finger.Metacarpal.IsTracking) count++;
        if (finger.Proximal.IsTracking) count++;
        if (finger.Intermediate != null && finger.Intermediate.IsTracking) count++;
        if (finger.Distal.IsTracking) count++;
        if (finger.Tip.IsTracking) count++;
        return count >= 3;
    }

    private static float AverageCurlDegrees(Hand hand)
    {
        return (CurlDegrees(hand.Index)
                + CurlDegrees(hand.Middle)
                + CurlDegrees(hand.Ring)
                + CurlDegrees(hand.Pinky)) / 4f;
    }

    private static float CurlDegrees(Finger finger)
    {
        Span<float3> positions = stackalloc float3[5];
        int count = 0;
        if (finger.Metacarpal.IsTracking) positions[count++] = finger.Metacarpal.Position;
        if (finger.Proximal.IsTracking) positions[count++] = finger.Proximal.Position;
        if (finger.Intermediate != null && finger.Intermediate.IsTracking) positions[count++] = finger.Intermediate.Position;
        if (finger.Distal.IsTracking) positions[count++] = finger.Distal.Position;
        if (finger.Tip.IsTracking) positions[count++] = finger.Tip.Position;
        if (count < 3)
            return float.NaN;

        float3 previous = (positions[1] - positions[0]).Normalized;
        float sum = 0f;
        for (int i = 1; i < count - 1; i++)
        {
            float3 current = (positions[i + 1] - positions[i]).Normalized;
            sum += MathX.Angle(in previous, in current);
            previous = current;
        }
        return sum;
    }

    private static void Release(InteractionHandler handler, ProximityGrabState state)
    {
        state.ProximityGrabActive = false;
        state.LeftEngaged = false;
        state.RightEngaged = false;
        Methods.EndGrab.Invoke(handler, new object[] { false });
        Methods.GrabBlockActions.SetValue(handler, false);
    }
}