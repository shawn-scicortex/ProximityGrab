using System;
using System.Collections.Generic;
using Elements.Core;
using FrooxEngine;
using Renderite.Shared;

namespace ProximityGrabCustomGesture;

internal static class FistGesture
{
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

        if (!ProximityGrabCustomGestureMod.FistGrabEnabled)
            state.Engaged = false;
        if (!ProximityGrabCustomGestureMod.PrecisionGrabEnabled)
            state.PinchEngaged = false;

        Hand? ownHand = handler.Side.Value == Chirality.Left ? left : right;
        bool fist = UpdateHand(ownHand, ref state.Engaged);
        bool pinch = UpdatePinch(handler, ownHand, ref state.PinchEngaged, fist);

        if (!state.HasTrackingHands)
        {
            fist = false;
            pinch = false;
        }

        GrabGestureKind gesture = fist
            ? GrabGestureKind.Fist
            : pinch ? GrabGestureKind.Pinch : GrabGestureKind.None;

        if (state.ProximityGrabActive && gesture == GrabGestureKind.None)
        {
            Release(handler, state);
        }
        else if (!state.ProximityGrabActive && gesture != GrabGestureKind.None)
        {
            if (handler.Grabber?.IsHoldingObjects == true)
                return;
            state.ProximityGrabActive = true;
            state.ActiveGesture = gesture;
            state.GestureHand = ownHand;
            state.GestureDriven = true;
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
        float avg = AverageCurlDegrees(hand!);
        float min = MinCurlDegrees(hand!);
        bool engaged = prevEngaged
            ? avg > ProximityGrabCustomGestureMod.FistReleaseDegrees
              && min > ProximityGrabCustomGestureMod.FistMinReleaseDegrees
            : avg > ProximityGrabCustomGestureMod.FistEngageDegrees
              && min > ProximityGrabCustomGestureMod.FistMinEngageDegrees;
        prevEngaged = engaged;
        return engaged;
    }

    private static bool UpdatePinch(InteractionHandler handler, Hand? hand, ref bool prevEngaged, bool fistEngaged)
    {
        if (fistEngaged)
        {
            prevEngaged = false;
            return false;
        }
        if (!IsUsableForPinch(hand))
        {
            prevEngaged = false;
            return false;
        }
        var root = handler.LocalUserRoot;
        if (root == null)
        {
            prevEngaged = false;
            return false;
        }
        if (CurlDegrees(hand!.Index) >= ProximityGrabCustomGestureMod.PinchMaxIndexCurlDegrees)
        {
            prevEngaged = false;
            return false;
        }
        float3 indexTip = root.Slot.LocalPointToGlobal(hand!.Index.Tip.Position);
        float3 thumbTip = root.Slot.LocalPointToGlobal(hand!.Thumb.Tip.Position);
        float distance = (indexTip - thumbTip).Magnitude / root.GlobalScale;
        bool engaged = prevEngaged
            ? distance < ProximityGrabCustomGestureMod.PinchReleaseDistance
            : distance < ProximityGrabCustomGestureMod.PinchEngageDistance;
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

    private static bool IsUsableForPinch(Hand? hand)
    {
        return hand != null
               && IsUsableFinger(hand.Index)
               && IsUsableThumb(hand.Thumb)
               && hand.Index.Tip.IsTracking
               && hand.Thumb.Tip.IsTracking;
    }

    private static bool IsUsableThumb(Finger thumb)
    {
        int count = 0;
        if (thumb.Metacarpal.IsTracking) count++;
        if (thumb.Proximal.IsTracking) count++;
        if (thumb.Distal.IsTracking) count++;
        if (thumb.Tip.IsTracking) count++;
        return count >= 3;
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

    private static float MinCurlDegrees(Hand hand)
    {
        return MathX.Min(CurlDegrees(hand.Index), CurlDegrees(hand.Middle), CurlDegrees(hand.Ring), CurlDegrees(hand.Pinky));
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
        state.Engaged = false;
        state.PinchEngaged = false;
        state.GestureHand = null;
        state.ActiveGesture = GrabGestureKind.None;
        Methods.EndGrab.Invoke(handler, new object[] { false });
        Methods.GrabBlockActions.SetValue(handler, false);
    }
}