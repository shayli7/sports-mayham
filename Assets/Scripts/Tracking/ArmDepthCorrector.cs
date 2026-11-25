using System;
using SportsMayhem.Calibration;
using UnityEngine;

namespace SportsMayhem.Tracking
{
    /// <summary>
    /// Fixes arms pointing at the camera (e.g. a straight punch).
    ///
    /// A single camera judges depth poorly: an arm pointing at the lens looks short in the image and
    /// MediaPipe underestimates how far forward the hand is. But we know the real arm length (from
    /// calibration, or estimated), so when a bone looks shorter than it is, the missing length must be
    /// depth: z = sqrt(L^2 - xy^2). That depth is added towards the camera.
    /// </summary>
    public sealed class ArmDepthCorrector
    {
        [Serializable]
        public class Settings
        {
            public bool enabled = true;

            [Tooltip("Start correcting when a bone looks shorter than this fraction of its real length.")]
            [Range(0.5f, 1f)] public float startRatio = 0.85f;

            [Tooltip("Fully corrected when a bone looks this short (or shorter).")]
            [Range(0.2f, 0.9f)] public float fullRatio = 0.6f;
        }

        private float _upperEstimate = 0.28f;
        private float _foreEstimate = 0.26f;

        private static readonly PoseJoint[] LeftHand = { PoseJoint.LeftWrist, PoseJoint.LeftPinky, PoseJoint.LeftIndex, PoseJoint.LeftThumb };
        private static readonly PoseJoint[] RightHand = { PoseJoint.RightWrist, PoseJoint.RightPinky, PoseJoint.RightIndex, PoseJoint.RightThumb };

        public void Apply(PoseFrame pose, Settings settings)
        {
            if (!settings.enabled || !pose.hasPose) return;

            var calibration = PlayerCalibration.Current;
            UpdateEstimates(pose);
            var upper = calibration.isCalibrated ? calibration.upperArmLength : _upperEstimate;
            var fore = calibration.isCalibrated ? calibration.forearmLength : _foreEstimate;

            CorrectArm(pose, PoseJoint.LeftShoulder, PoseJoint.LeftElbow, LeftHand, upper, fore, settings);
            CorrectArm(pose, PoseJoint.RightShoulder, PoseJoint.RightElbow, RightHand, upper, fore, settings);
        }

        private static void CorrectArm(PoseFrame pose, PoseJoint shoulder, PoseJoint elbow, PoseJoint[] hand,
            float upperLength, float foreLength, Settings settings)
        {
            if (!pose.IsVisible(shoulder, 0.3f) || !pose.IsVisible(elbow, 0.3f)) return;

            var s = pose[shoulder];
            var e = pose[elbow];
            var w = pose[hand[0]];

            // Upper arm, then forearm (relative to the corrected elbow). Moving a joint moves everything after it.
            var newElbow = s + CorrectBone(e - s, upperLength, settings);
            var elbowShift = newElbow - e;
            pose.position[(int)elbow] = newElbow;

            if (!pose.IsVisible(hand[0], 0.3f))
            {
                ShiftHand(pose, hand, elbowShift);
                return;
            }

            var shiftedWrist = w + elbowShift;
            var newWrist = newElbow + CorrectBone(shiftedWrist - newElbow, foreLength, settings);
            ShiftHand(pose, hand, newWrist - w);
        }

        private static void ShiftHand(PoseFrame pose, PoseJoint[] hand, Vector3 shift)
        {
            foreach (var joint in hand) pose.position[(int)joint] += shift;
        }

        private static Vector3 CorrectBone(Vector3 bone, float length, Settings settings)
        {
            if (length < 0.05f) return bone;
            var flat = Mathf.Sqrt(bone.x * bone.x + bone.y * bone.y);
            var ratio = flat / length;

            // 0 = bone looks full length (no change), 1 = looks very short (full correction).
            var weight = Mathf.InverseLerp(settings.startRatio, settings.fullRatio, ratio);
            if (weight <= 0f) return bone;
            weight = weight * weight * (3f - 2f * weight); // smoothstep, avoids sudden jumps

            var neededDepth = Mathf.Sqrt(Mathf.Max(0f, length * length - flat * flat));
            // Arms reach forward far more often than backward: only trust "backward" if MediaPipe is clearly sure.
            var sign = bone.z < -0.08f ? -1f : 1f;
            var depth = Mathf.Max(Mathf.Abs(bone.z), neededDepth);
            bone.z = Mathf.Lerp(bone.z, sign * depth, weight);
            return bone;
        }

        /// <summary>Without calibration: learn bone lengths from frames where the arm is clearly side-on (looks long).</summary>
        private void UpdateEstimates(PoseFrame pose)
        {
            Learn(pose, PoseJoint.LeftShoulder, PoseJoint.LeftElbow, ref _upperEstimate);
            Learn(pose, PoseJoint.RightShoulder, PoseJoint.RightElbow, ref _upperEstimate);
            Learn(pose, PoseJoint.LeftElbow, PoseJoint.LeftWrist, ref _foreEstimate);
            Learn(pose, PoseJoint.RightElbow, PoseJoint.RightWrist, ref _foreEstimate);
        }

        private static void Learn(PoseFrame pose, PoseJoint a, PoseJoint b, ref float estimate)
        {
            if (!pose.IsVisible(a, 0.7f) || !pose.IsVisible(b, 0.7f)) return;
            var length = Vector3.Distance(pose[a], pose[b]);
            if (length < 0.15f || length > 0.45f) return;
            // Rise quickly towards longer readings (foreshortened arms only ever look shorter), fall slowly.
            estimate = Mathf.Lerp(estimate, length, length > estimate ? 0.05f : 0.002f);
        }
    }
}
