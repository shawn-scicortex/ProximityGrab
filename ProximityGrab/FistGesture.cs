using System;
using System.Collections.Generic;
using Elements.Core;
using FrooxEngine;
using Renderite.Shared;

namespace ProximityGrab;

internal static class FistGesture
{
    private static readonly List<Hand> Devices = new();

    private static bool LastGestureMode = true;

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

        Hand? ownHand = handler.Side.Value == Chirality.Left ? left : right;
        // Mirror the RML GestureMode key into this hand's menu field so config-UI
        // edits appear within a frame, and rebuild bindings on mode transitions so
        // the controller grip bindings follow immediately from any source.
        ValueField<bool> gestureField = MenuPatches.GetGestureField(handler);
        if (gestureField.Value.Value != ProximityGrabMod.GestureMode)
            gestureField.Value.Value = ProximityGrabMod.GestureMode;
        if (ProximityGrabMod.GestureMode != LastGestureMode)
        {
            LastGestureMode = ProximityGrabMod.GestureMode;
            handler.Input.InvalidateBindings();
        }
        if (!ProximityGrabMod.GestureMode)
        {
            // Gesture mode off: fully-stock behavior. Clear hysteresis memories so
            // re-enabling starts clean; any in-flight gesture grab ends via Release below.
            state.FistEngaged = false;
            state.PinchEngaged = false;
            state.GestureDriven = false;
        }
        bool fistRaw = ProximityGrabMod.GestureMode && UpdateFist(ownHand, ref state.FistEngaged);
        bool pinchRaw = ProximityGrabMod.GestureMode && UpdatePinch(handler, ownHand, ref state.PinchEngaged);
        bool fist = ProximityGrabMod.FistGrabEnabled && fistRaw;
        bool pinch = ProximityGrabMod.PrecisionGrabEnabled && pinchRaw && !fist;

        if (!state.HasTrackingHands)
        {
            fist = false;
            pinch = false;
            state.GestureDriven = false;
            state.GestureHand = null;
        }

        GrabGestureKind gesture = fist
            ? GrabGestureKind.Fist
            : pinch ? GrabGestureKind.Pinch : GrabGestureKind.None;

        DrawDebug(handler, ownHand, state);

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

    private static bool UpdateFist(Hand? hand, ref bool prevEngaged)
    {
        if (!IsUsable(hand))
        {
            prevEngaged = false;
            return false;
        }
        float avg = AverageCurlDegrees(hand!);
        float min = MinCurlDegrees(hand!);
        float minJoint = MinJointCurlDegrees(hand!);
        bool engaged = prevEngaged
            ? avg > ProximityGrabMod.FistReleaseDegrees
              && min > ProximityGrabMod.FistMinReleaseDegrees
              && minJoint > ProximityGrabMod.FistMinJointReleaseDegrees
            : avg > ProximityGrabMod.FistEngageDegrees
              && min > ProximityGrabMod.FistMinEngageDegrees
              && minJoint > ProximityGrabMod.FistMinJointEngageDegrees;
        prevEngaged = engaged;
        return engaged;
    }

    private static bool UpdatePinch(InteractionHandler handler, Hand? hand, ref bool prevEngaged)
    {
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
        if (CurlDegrees(hand!.Index) >= ProximityGrabMod.PinchMaxIndexCurlDegrees)
        {
            prevEngaged = false;
            return false;
        }
        float3 indexTip = root.Slot.LocalPointToGlobal(hand!.Index.Tip.Position);
        float3 thumbTip = root.Slot.LocalPointToGlobal(hand!.Thumb.Tip.Position);
        float distance = (indexTip - thumbTip).Magnitude / root.GlobalScale;
        bool engaged = prevEngaged
            ? distance < ProximityGrabMod.PinchReleaseDistance
            : distance < ProximityGrabMod.PinchEngageDistance;
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

    // Smallest valid per-joint curl across the four fingers; untracked joints are skipped.
    // NaN (fail-safe: blocks the gate) only if no finger yields a valid joint.
    private static float MinJointCurlDegrees(Hand hand)
    {
        Span<float> joints = stackalloc float[3];
        float min = float.PositiveInfinity;
        int valid = 0;
        AccumulateMinJoint(hand.Index, joints, ref min, ref valid);
        AccumulateMinJoint(hand.Middle, joints, ref min, ref valid);
        AccumulateMinJoint(hand.Ring, joints, ref min, ref valid);
        AccumulateMinJoint(hand.Pinky, joints, ref min, ref valid);
        return valid > 0 ? min : float.NaN;
    }

    private static void AccumulateMinJoint(Finger finger, Span<float> joints, ref float min, ref int valid)
    {
        int count = JointCurlDegrees(finger, joints);
        for (int i = 0; i < count - 2 && i < joints.Length; i++)
        {
            if (float.IsNaN(joints[i]))
                continue;
            if (joints[i] < min)
                min = joints[i];
            valid++;
        }
    }

    private static float CurlDegrees(Finger finger)
    {
        Span<float> joints = stackalloc float[3];
        int count = JointCurlDegrees(finger, joints);
        if (count < 3)
            return float.NaN;

        float sum = 0f;
        for (int i = 0; i < count - 2; i++)
            sum += joints[i];
        return sum;
    }

    // Per-joint curl angles (MCP/PIP/DIP when fully tracked); returns tracked segment count.
    // Joints beyond count - 2 are untouched; count < 3 means no valid angles.
    private static int JointCurlDegrees(Finger finger, Span<float> joints)
    {
        Span<float3> positions = stackalloc float3[5];
        int count = 0;
        if (finger.Metacarpal.IsTracking) positions[count++] = finger.Metacarpal.Position;
        if (finger.Proximal.IsTracking) positions[count++] = finger.Proximal.Position;
        if (finger.Intermediate != null && finger.Intermediate.IsTracking) positions[count++] = finger.Intermediate.Position;
        if (finger.Distal.IsTracking) positions[count++] = finger.Distal.Position;
        if (finger.Tip.IsTracking) positions[count++] = finger.Tip.Position;
        if (count < 3)
            return count;

        float3 previous = (positions[1] - positions[0]).Normalized;
        int j = 0;
        for (int i = 1; i < count - 1; i++)
        {
            float3 current = (positions[i + 1] - positions[i]).Normalized;
            if (j < joints.Length)
                joints[j++] = MathX.Angle(in previous, in current);
            previous = current;
        }
        return count;
    }

    private static void DrawDebug(InteractionHandler handler, Hand? hand, ProximityGrabState state)
    {
        try
        {
            if (!ProximityGrabMod.DebugShowPinch || !ProximityGrabMod.GestureMode || hand == null)
                return;
            var root = handler.LocalUserRoot;
            if (root == null)
                return;
            float distance = float.NaN;
            if (hand.Index.Tip.IsTracking && hand.Thumb.Tip.IsTracking)
            {
                float3 indexTip = root.Slot.LocalPointToGlobal(hand.Index.Tip.Position);
                float3 thumbTip = root.Slot.LocalPointToGlobal(hand.Thumb.Tip.Position);
                distance = (indexTip - thumbTip).Magnitude / root.GlobalScale;
            }
            string side = handler.Side.Value == Chirality.Left ? "L" : "R";
            string p = PrecisionGrab.LastAttemptDetail;
            if (p.Length > 36)
                p = p.Substring(0, 36);
            float fistAvg = AverageCurlDegrees(hand);
            float fistMin = MinCurlDegrees(hand);
            float fistMinJoint = MinJointCurlDegrees(hand);
            float fistThreshold = state.FistEngaged
                ? ProximityGrabMod.FistReleaseDegrees
                : ProximityGrabMod.FistEngageDegrees;
            string fistLine = $"{side} Fist avg:{FormatDegrees(fistAvg)}/{fistThreshold:0} min:{FormatDegrees(fistMin)} jm:{FormatDegrees(fistMinJoint)} eng:{(state.FistEngaged ? "Y" : "N")}";
            string pinchLine = $"{side} Pinch d:{distance:F3} idx:{CurlDegrees(hand.Index):F0}/{ProximityGrabMod.PinchMaxIndexCurlDegrees:0} th:{ThumbTrackingCount(hand)} eng:{(state.PinchEngaged ? "Y" : "N")}";
            Span<float> indexJoints = stackalloc float[3];
            int indexTracked = JointCurlDegrees(hand.Index, indexJoints);
            string indexLine = $"{side} Idx j:{FormatJoint(indexJoints, indexTracked, 0)}/{FormatJoint(indexJoints, indexTracked, 1)}/{FormatJoint(indexJoints, indexTracked, 2)} tot:{FormatDegrees(CurlDegrees(hand.Index))} n:{indexTracked}";
            Slot frame = root.Slot;
            float3 wristWorld = frame.LocalPointToGlobal(hand.Wrist.Position);
            floatQ wristRot = frame.LocalRotationToGlobal(hand.Wrist.Rotation);
            float3 wristMarker = wristWorld;
            float3 debugAnchorLine = wristMarker + wristRot * float3.Up * 0.12f;
            float3 debugAnchorLine2 = debugAnchorLine + wristRot * float3.Up * 0.11f;
            float3 debugAnchorLine3 = debugAnchorLine2 + wristRot * float3.Up * 0.11f;
            var fistColor = state.FistEngaged ? colorX.Green : colorX.White;
            var pinchColor = state.PinchEngaged ? colorX.Green : colorX.White;
            var indexColor = colorX.White;
            handler.Debug.Text(in debugAnchorLine, fistLine, 0.08f, in fistColor, 0f, true);
            handler.Debug.Text(in debugAnchorLine2, pinchLine, 0.08f, in pinchColor, 0f, true);
            handler.Debug.Text(in debugAnchorLine3, indexLine, 0.08f, in indexColor, 0f, true);
            if (hand.Index.Tip.IsTracking && hand.Thumb.Tip.IsTracking)
            {
                float3 indexMarker = wristWorld + wristRot * hand.Index.Tip.Position;
                float3 thumbMarker = wristWorld + wristRot * hand.Thumb.Tip.Position;
                float3 originMarker = MathX.Lerp(indexMarker, thumbMarker, 0.5f);
                float size = 0.06f;
                colorX cIndex = colorX.Red;
                colorX cThumb = colorX.Green;
                colorX cOrigin = colorX.Yellow;
                colorX cWrist = colorX.Blue;
                colorX cSphere = colorX.Orange.SetA(0.1f);
                handler.Debug.Text(in indexMarker, "I", size, in cIndex, 0f, true);
                handler.Debug.Text(in thumbMarker, "T", size, in cThumb, 0f, true);
                handler.Debug.Text(in originMarker, "O", size, in cOrigin, 0f, true);
                handler.Debug.Text(in wristMarker, "W", size, in cWrist, 0f, true);
                handler.Debug.Sphere(in originMarker, ProximityGrabMod.PrecisionMaxRadius, in cSphere, local: true);
            }
            // Fist grab sphere: mirrors the engine non-laser overlap test
            // (InteractionHandler GRAB_RADIUS at Grabber slot, scaled by user root).
            var grabber = handler.Grabber;
            if (grabber != null)
            {
                float3 grabCenter = grabber.Slot.GlobalPosition;
                float grabRadius = InteractionHandler.GRAB_RADIUS * (handler.LocalUserRoot?.GlobalScale ?? 1f);
                colorX fistSphere = colorX.Cyan.SetA(0.08f);
                handler.Debug.Sphere(in grabCenter, grabRadius, in fistSphere, local: true);
            }
        }
        catch (Exception e)
        {
            UniLog.Error("ProximityGrab debug overlay failed: " + e);
        }
    }

    private static string FormatDegrees(float degrees) =>
        float.IsNaN(degrees) ? "---" : $"{degrees:F0}";

    private static string FormatJoint(Span<float> joints, int trackedCount, int joint) =>
        joint < trackedCount - 2 ? $"{joints[joint]:F0}" : "-";

    private static int ThumbTrackingCount(Hand hand)
    {
        int count = 0;
        if (hand.Thumb.Metacarpal.IsTracking) count++;
        if (hand.Thumb.Proximal.IsTracking) count++;
        if (hand.Thumb.Distal.IsTracking) count++;
        if (hand.Thumb.Tip.IsTracking) count++;
        return count;
    }

    private static void Release(InteractionHandler handler, ProximityGrabState state)
    {
        state.ProximityGrabActive = false;
        state.FistEngaged = false;
        state.PinchEngaged = false;
        state.GestureHand = null;
        state.ActiveGesture = GrabGestureKind.None;
        Methods.EndGrab.Invoke(handler, new object[] { false });
        Methods.GrabBlockActions.SetValue(handler, false);
    }
}
