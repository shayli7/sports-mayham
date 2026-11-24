using System;
using SportsMayhem.Tracking;
using UnityEngine;

namespace SportsMayhem.Calibration
{
    /// <summary>What's wrong with the player's position, most important first.</summary>
    public enum FramingIssue
    {
        None,
        NoPerson,
        TooDark,
        TooClose,
        TooFar,
        TooFarLeft,
        TooFarRight,
        CameraTooLow,
        CameraTooHigh,
        HandsNotVisible,
    }

    /// <summary>Checks on a pose that calibration and the tracking-lost pause share.</summary>
    public static class BodyChecks
    {
        [Serializable]
        public class Limits
        {
            [Tooltip("Shoulder width as a fraction of the image width. Bigger = closer to the camera.")]
            public float minScreenShoulderWidth = 0.13f;
            public float maxScreenShoulderWidth = 0.32f;

            [Tooltip("Allowed horizontal range for the shoulder midpoint (0..1, mirror view).")]
            public float minCenterX = 0.3f;
            public float maxCenterX = 0.7f;

            [Tooltip("Allowed vertical range for the shoulder midpoint (0 = bottom, 1 = top).")]
            public float minCenterY = 0.3f;
            public float maxCenterY = 0.75f;

            [Tooltip("Average camera brightness (0..1) below which the room counts as too dark.")]
            public float minBrightness = 0.15f;

            [Range(0f, 1f)] public float minVisibility = 0.5f;
        }

        public static Vector2 ShoulderCenter(PoseFrame pose) =>
            (pose.ScreenPoint(PoseJoint.LeftShoulder) + pose.ScreenPoint(PoseJoint.RightShoulder)) * 0.5f;

        public static float ScreenShoulderWidth(PoseFrame pose) =>
            Mathf.Abs(pose.ScreenPoint(PoseJoint.RightShoulder).x - pose.ScreenPoint(PoseJoint.LeftShoulder).x);

        /// <summary>
        /// Full check used while calibrating: person found, at the right distance, centered, hands visible.
        /// Pass brightness &lt; 0 when unknown (e.g. during playback).
        /// </summary>
        public static FramingIssue CheckFraming(PoseFrame pose, Limits limits, float brightness)
        {
            var basic = CheckTracking(pose, limits, brightness);
            if (basic != FramingIssue.None) return basic;

            var width = ScreenShoulderWidth(pose);
            if (width > limits.maxScreenShoulderWidth) return FramingIssue.TooClose;
            if (width < limits.minScreenShoulderWidth) return FramingIssue.TooFar;

            var center = ShoulderCenter(pose);
            if (center.x < limits.minCenterX) return FramingIssue.TooFarLeft;
            if (center.x > limits.maxCenterX) return FramingIssue.TooFarRight;
            if (center.y > limits.maxCenterY) return FramingIssue.CameraTooLow;
            if (center.y < limits.minCenterY) return FramingIssue.CameraTooHigh;

            if (!pose.IsVisible(PoseJoint.LeftWrist, limits.minVisibility) ||
                !pose.IsVisible(PoseJoint.RightWrist, limits.minVisibility))
            {
                return FramingIssue.HandsNotVisible;
            }

            return FramingIssue.None;
        }

        /// <summary>Lighter check used during play: is the head and both shoulders still trackable?</summary>
        public static FramingIssue CheckTracking(PoseFrame pose, Limits limits, float brightness)
        {
            if (brightness >= 0f && brightness < limits.minBrightness) return FramingIssue.TooDark;
            if (!pose.hasPose) return FramingIssue.NoPerson;

            if (!pose.IsVisible(PoseJoint.Nose, limits.minVisibility) ||
                !pose.IsVisible(PoseJoint.LeftShoulder, limits.minVisibility) ||
                !pose.IsVisible(PoseJoint.RightShoulder, limits.minVisibility))
            {
                return FramingIssue.NoPerson;
            }

            // Shoulders cut off by the image edge.
            var left = pose.ScreenPoint(PoseJoint.LeftShoulder).x;
            var right = pose.ScreenPoint(PoseJoint.RightShoulder).x;
            if (Mathf.Min(left, right) < 0.02f) return FramingIssue.TooFarLeft;
            if (Mathf.Max(left, right) > 0.98f) return FramingIssue.TooFarRight;

            return FramingIssue.None;
        }

        /// <summary>Instruction for the player. Directions are for the mirror view they see.</summary>
        public static string Describe(FramingIssue issue)
        {
            switch (issue)
            {
                case FramingIssue.None: return "Perfect, hold still";
                case FramingIssue.NoPerson: return "Step in front of the camera so your head and shoulders are visible";
                case FramingIssue.TooDark: return "It's too dark. Turn on a light in front of you";
                case FramingIssue.TooClose: return "Step back a little";
                case FramingIssue.TooFar: return "Come a little closer";
                case FramingIssue.TooFarLeft: return "Move to your right";
                case FramingIssue.TooFarRight: return "Move to your left";
                case FramingIssue.CameraTooLow: return "Tilt the phone up, or step back";
                case FramingIssue.CameraTooHigh: return "Tilt the phone down, or step back";
                case FramingIssue.HandsNotVisible: return "Make sure both hands are visible";
                default: return issue.ToString();
            }
        }

        /// <summary>
        /// Is the player holding a T-pose: both arms straight out to the sides at shoulder height?
        /// </summary>
        public static bool IsTPose(PoseFrame pose, float minVisibility)
        {
            return IsArmOut(pose, PoseJoint.LeftShoulder, PoseJoint.LeftElbow, PoseJoint.LeftWrist, -1f, minVisibility) &&
                   IsArmOut(pose, PoseJoint.RightShoulder, PoseJoint.RightElbow, PoseJoint.RightWrist, +1f, minVisibility);
        }

        private static bool IsArmOut(PoseFrame pose, PoseJoint shoulder, PoseJoint elbow, PoseJoint wrist, float side, float minVisibility)
        {
            if (!pose.IsVisible(shoulder, minVisibility) || !pose.IsVisible(elbow, minVisibility) || !pose.IsVisible(wrist, minVisibility))
            {
                return false;
            }

            var s = pose[shoulder];
            var e = pose[elbow];
            var w = pose[wrist];

            // Arm roughly straight: elbow angle over 140 degrees.
            if (Vector3.Angle(s - e, w - e) < 140f) return false;

            // Pointing sideways (x is the player's right) and roughly level.
            var direction = (w - s).normalized;
            return direction.x * side > 0.7f && Mathf.Abs(direction.y) < 0.45f;
        }
    }
}
