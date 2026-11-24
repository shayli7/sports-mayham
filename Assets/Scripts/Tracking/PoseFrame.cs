using System;
using UnityEngine;

namespace SportsMayhem.Tracking
{
    /// <summary>
    /// One tracked pose. This is the only pose data gameplay code should read.
    ///
    /// Coordinate spaces:
    /// - <see cref="position"/>: meters in "player space". The origin is the midpoint between the
    ///   shoulders. x = the player's right, y = up, z = forward (towards the camera).
    ///   This matches Unity's axes, so a camera placed behind an avatar facing +z shows a mirror view.
    /// - <see cref="screen"/>: 0..1 across the screen as a mirror view (x right, y up).
    ///   Use it for on-screen guides such as the calibration outline.
    /// </summary>
    [Serializable]
    public sealed class PoseFrame
    {
        public const int JointCount = PoseJoints.Count;

        /// <summary>Seconds. Only differences between frames matter.</summary>
        public double time;

        /// <summary>False when no person was found in this camera frame.</summary>
        public bool hasPose;

        /// <summary>Distance between the shoulders in meters (handy for scaling gestures to body size).</summary>
        public float shoulderWidth;

        public readonly Vector3[] position = new Vector3[JointCount];
        public readonly Vector2[] screen = new Vector2[JointCount];
        public readonly float[] visibility = new float[JointCount];

        public Vector3 this[PoseJoint joint] => position[(int)joint];

        public Vector2 ScreenPoint(PoseJoint joint) => screen[(int)joint];

        public float Visibility(PoseJoint joint) => visibility[(int)joint];

        public bool IsVisible(PoseJoint joint, float minVisibility = 0.5f) =>
            hasPose && visibility[(int)joint] >= minVisibility;

        public void CopyFrom(PoseFrame other)
        {
            time = other.time;
            hasPose = other.hasPose;
            shoulderWidth = other.shoulderWidth;
            Array.Copy(other.position, position, JointCount);
            Array.Copy(other.screen, screen, JointCount);
            Array.Copy(other.visibility, visibility, JointCount);
        }

        public void Clear(double atTime)
        {
            time = atTime;
            hasPose = false;
            shoulderWidth = 0f;
            Array.Clear(position, 0, JointCount);
            Array.Clear(screen, 0, JointCount);
            Array.Clear(visibility, 0, JointCount);
        }

        /// <summary>Moves the origin to the midpoint between the shoulders and updates <see cref="shoulderWidth"/>.</summary>
        public void CenterOnShoulders()
        {
            if (!hasPose) return;
            var left = position[(int)PoseJoint.LeftShoulder];
            var right = position[(int)PoseJoint.RightShoulder];
            var mid = (left + right) * 0.5f;
            shoulderWidth = Vector3.Distance(left, right);
            for (var i = 0; i < JointCount; i++)
            {
                position[i] -= mid;
            }
        }
    }
}
