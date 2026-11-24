using System;
using SportsMayhem.Calibration;
using SportsMayhem.Tracking;
using UnityEngine;

namespace SportsMayhem.Avatar
{
    /// <summary>
    /// Placeholder player avatar: a floating robot built from primitives at runtime.
    /// Arms follow the player's arms (two-bone IK, scaled to the robot's size), the head turns with the
    /// player's head, and the body leans, sways and ducks with the player.
    ///
    /// The robot faces +z (away from a camera placed behind it), so the player sees it from behind,
    /// and the player's right hand moves the robot's right hand on the right side of the screen.
    /// Place this object where the robot's shoulder midpoint should be.
    /// </summary>
    public sealed class RobotAvatar : MonoBehaviour
    {
        [Serializable]
        public class Look
        {
            public Color body = new Color(0.85f, 0.86f, 0.9f);
            public Color accent = new Color(1f, 0.55f, 0.1f);
            public Color visor = new Color(0.1f, 0.9f, 1f);
            public Color leftGlove = new Color(0.25f, 0.55f, 1f);
            public Color rightGlove = new Color(1f, 0.3f, 0.3f);
        }

        [SerializeField] private PoseTracker tracker;
        [SerializeField] private Look look = new Look();

        [Header("Proportions (meters)")]
        [SerializeField] private float shoulderWidth = 0.5f;
        [SerializeField] private float upperArmLength = 0.3f;
        [SerializeField] private float forearmLength = 0.3f;
        [Tooltip("Show upper arms and forearms. Off = floating gloves only.")]
        [SerializeField] private bool showArms = true;

        [Header("Body movement")]
        [Tooltip("How much the robot follows the player stepping sideways / ducking. 0 = stays in place.")]
        [Range(0f, 2f)] [SerializeField] private float bodyMotionScale = 1f;
        [SerializeField] private float maxSideStep = 0.7f;
        [SerializeField] private float maxDuck = 0.5f;
        [Tooltip("Degrees. Limits how far the torso can lean or twist.")]
        [SerializeField] private float maxTorsoAngle = 35f;

        [Header("Responsiveness")]
        [Tooltip("Higher = snappier. The pose is already smoothed; this only blends small gaps.")]
        [SerializeField] private float followSpeed = 30f;
        [Range(0f, 1f)] [SerializeField] private float minVisibility = 0.4f;

        private Transform _rig;      // moves with side steps / ducking
        private Transform _torso;    // leans / twists
        private Transform _head;
        private Transform _leftGlove, _rightGlove;
        private Transform _leftUpper, _leftFore, _rightUpper, _rightFore;
        private Transform _leftShoulderBall, _rightShoulderBall;

        private Vector3 _leftHand, _rightHand, _leftElbow, _rightElbow;
        private Quaternion _torsoRotation = Quaternion.identity;
        private Quaternion _headRotation = Quaternion.identity;
        private Vector3 _bodyOffset;
        private float _estimatedPlayerArm = 0.54f;

        private void Awake()
        {
            Build();
            _leftHand = GuardPosition(-1f);
            _rightHand = GuardPosition(+1f);
        }

        // ---------- Building the robot ----------

        private void Build()
        {
            _rig = new GameObject("Rig").transform;
            _rig.SetParent(transform, false);

            _torso = new GameObject("Torso").transform;
            _torso.SetParent(_rig, false);

            // Chunky body hanging below the shoulders, with a hover ring instead of legs.
            Part(PrimitiveType.Cube, "Chest", _torso, new Vector3(0f, -0.25f, 0f), new Vector3(shoulderWidth * 0.95f, 0.5f, 0.32f), look.body);
            Part(PrimitiveType.Cube, "Belly Plate", _torso, new Vector3(0f, -0.3f, 0.165f), new Vector3(shoulderWidth * 0.5f, 0.25f, 0.02f), look.accent);
            Part(PrimitiveType.Cylinder, "Waist", _torso, new Vector3(0f, -0.58f, 0f), new Vector3(0.3f, 0.08f, 0.3f), look.accent);
            Part(PrimitiveType.Cylinder, "Hover Ring", _torso, new Vector3(0f, -0.72f, 0f), new Vector3(0.42f, 0.02f, 0.42f), look.visor);

            _leftShoulderBall = Part(PrimitiveType.Sphere, "Left Shoulder", _torso, new Vector3(-shoulderWidth * 0.5f, 0f, 0f), Vector3.one * 0.15f, look.accent);
            _rightShoulderBall = Part(PrimitiveType.Sphere, "Right Shoulder", _torso, new Vector3(shoulderWidth * 0.5f, 0f, 0f), Vector3.one * 0.15f, look.accent);

            // Head on a short neck; visor and antenna make the facing direction readable.
            Part(PrimitiveType.Cylinder, "Neck", _torso, new Vector3(0f, 0.06f, 0f), new Vector3(0.1f, 0.05f, 0.1f), look.accent);
            _head = new GameObject("Head").transform;
            _head.SetParent(_torso, false);
            _head.localPosition = new Vector3(0f, 0.12f, 0f);
            Part(PrimitiveType.Cube, "Skull", _head, new Vector3(0f, 0.16f, 0f), new Vector3(0.3f, 0.28f, 0.28f), look.body);
            Part(PrimitiveType.Cube, "Visor", _head, new Vector3(0f, 0.18f, 0.14f), new Vector3(0.24f, 0.08f, 0.02f), look.visor);
            Part(PrimitiveType.Cylinder, "Antenna", _head, new Vector3(0f, 0.36f, -0.05f), new Vector3(0.02f, 0.07f, 0.02f), look.accent);
            Part(PrimitiveType.Sphere, "Antenna Tip", _head, new Vector3(0f, 0.44f, -0.05f), Vector3.one * 0.05f, look.visor);

            // Arms live directly under the rig (not the torso) because IK places them in rig space.
            _leftUpper = Part(PrimitiveType.Capsule, "Left Upper Arm", _rig, Vector3.zero, Vector3.one * 0.08f, look.body);
            _leftFore = Part(PrimitiveType.Capsule, "Left Forearm", _rig, Vector3.zero, Vector3.one * 0.07f, look.body);
            _rightUpper = Part(PrimitiveType.Capsule, "Right Upper Arm", _rig, Vector3.zero, Vector3.one * 0.08f, look.body);
            _rightFore = Part(PrimitiveType.Capsule, "Right Forearm", _rig, Vector3.zero, Vector3.one * 0.07f, look.body);

            _leftGlove = Glove("Left Glove", look.leftGlove);
            _rightGlove = Glove("Right Glove", look.rightGlove);
        }

        private Transform Glove(string gloveName, Color color)
        {
            var glove = new GameObject(gloveName).transform;
            glove.SetParent(_rig, false);
            Part(PrimitiveType.Sphere, "Fist", glove, Vector3.zero, new Vector3(0.17f, 0.15f, 0.2f), color);
            Part(PrimitiveType.Cylinder, "Cuff", glove, new Vector3(0f, 0f, -0.1f), new Vector3(0.13f, 0.03f, 0.13f), look.body, Quaternion.Euler(90f, 0f, 0f));
            return glove;
        }

        private static Transform Part(PrimitiveType type, string partName, Transform parent, Vector3 localPosition, Vector3 localScale, Color color)
            => Part(type, partName, parent, localPosition, localScale, color, Quaternion.identity);

        private static Transform Part(PrimitiveType type, string partName, Transform parent, Vector3 localPosition, Vector3 localScale, Color color, Quaternion localRotation)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = partName;
            Destroy(go.GetComponent<Collider>());
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            t.localRotation = localRotation;
            t.localScale = localScale;
            go.GetComponent<Renderer>().material.color = color;
            return t;
        }

        // ---------- Following the player ----------

        private void LateUpdate()
        {
            var dt = Time.unscaledDeltaTime; // keep mirroring the player while the game is paused
            var blend = 1f - Mathf.Exp(-followSpeed * dt);
            var pose = tracker != null ? tracker.Current : null;
            var hasPose = pose != null && pose.hasPose;

            // Body: side steps / ducking (from the screen position) and lean / twist (from the shoulder line).
            var targetOffset = hasPose ? BodyOffset(pose) : Vector3.zero;
            _bodyOffset = Vector3.Lerp(_bodyOffset, targetOffset, blend * 0.5f);
            _rig.localPosition = _bodyOffset;

            var targetTorso = hasPose ? TorsoRotation(pose) : Quaternion.identity;
            _torsoRotation = Quaternion.Slerp(_torsoRotation, targetTorso, blend);
            _torso.localRotation = _torsoRotation;

            var targetHead = hasPose ? HeadRotation(pose) : _torsoRotation;
            _headRotation = Quaternion.Slerp(_headRotation, targetHead, blend);
            _head.rotation = transform.rotation * _headRotation; // head turns relative to the world, not the lean

            // Arms.
            var calibration = PlayerCalibration.Current;
            UpdateArmEstimate(pose, hasPose);
            var playerArm = calibration.isCalibrated ? calibration.ArmLength : _estimatedPlayerArm;
            var robotArm = upperArmLength + forearmLength;
            var scale = robotArm / Mathf.Max(playerArm, 0.2f);

            UpdateArm(pose, hasPose, -1f, PoseJoint.LeftShoulder, PoseJoint.LeftElbow, PoseJoint.LeftWrist, PoseJoint.LeftIndex,
                scale, blend, ref _leftHand, ref _leftElbow, _leftUpper, _leftFore, _leftGlove);
            UpdateArm(pose, hasPose, +1f, PoseJoint.RightShoulder, PoseJoint.RightElbow, PoseJoint.RightWrist, PoseJoint.RightIndex,
                scale, blend, ref _rightHand, ref _rightElbow, _rightUpper, _rightFore, _rightGlove);
        }

        private Vector3 BodyOffset(PoseFrame pose)
        {
            var calibration = PlayerCalibration.Current;
            var screenWidth = BodyChecks.ScreenShoulderWidth(pose);
            if (screenWidth < 0.02f) return _bodyOffset;

            // Measure movement in "shoulder widths" so it's independent of distance to the camera.
            var delta = BodyChecks.ShoulderCenter(pose) - calibration.screenCenter;
            var aspect = tracker.ImageAspect;
            var side = delta.x / screenWidth * shoulderWidth * bodyMotionScale;
            var up = delta.y / aspect / screenWidth * shoulderWidth * bodyMotionScale;

            return new Vector3(Mathf.Clamp(side, -maxSideStep, maxSideStep), Mathf.Clamp(up, -maxDuck, 0.15f), 0f);
        }

        private Quaternion TorsoRotation(PoseFrame pose)
        {
            var right = pose[PoseJoint.RightShoulder] - pose[PoseJoint.LeftShoulder];
            if (right.sqrMagnitude < 1e-6f) return _torsoRotation;
            right.Normalize();

            var forward = Vector3.Cross(right, Vector3.up);
            if (forward.sqrMagnitude < 1e-6f) return _torsoRotation;
            var up = Vector3.Cross(forward, right);
            var rotation = Quaternion.LookRotation(forward, up);
            return Quaternion.RotateTowards(Quaternion.identity, rotation, maxTorsoAngle);
        }

        private Quaternion HeadRotation(PoseFrame pose)
        {
            if (!pose.IsVisible(PoseJoint.Nose, minVisibility)) return _headRotation;

            var earsVisible = pose.IsVisible(PoseJoint.LeftEar, minVisibility) && pose.IsVisible(PoseJoint.RightEar, minVisibility);
            var back = earsVisible ? (pose[PoseJoint.LeftEar] + pose[PoseJoint.RightEar]) * 0.5f : Vector3.zero;

            var forward = pose[PoseJoint.Nose] - back;
            var up = earsVisible ? back : Vector3.up; // from the shoulder midpoint (origin) up to the ears
            if (forward.sqrMagnitude < 1e-6f || up.sqrMagnitude < 1e-6f) return _headRotation;

            var rotation = Quaternion.LookRotation(forward.normalized, up.normalized);
            return Quaternion.RotateTowards(Quaternion.identity, rotation, 60f);
        }

        /// <summary>Without calibration, estimate the player's arm length from recent frames.</summary>
        private void UpdateArmEstimate(PoseFrame pose, bool hasPose)
        {
            if (!hasPose) return;
            EstimateFrom(pose, PoseJoint.LeftShoulder, PoseJoint.LeftElbow, PoseJoint.LeftWrist);
            EstimateFrom(pose, PoseJoint.RightShoulder, PoseJoint.RightElbow, PoseJoint.RightWrist);
        }

        private void EstimateFrom(PoseFrame pose, PoseJoint s, PoseJoint e, PoseJoint w)
        {
            if (!pose.IsVisible(s, 0.7f) || !pose.IsVisible(e, 0.7f) || !pose.IsVisible(w, 0.7f)) return;
            var length = Vector3.Distance(pose[s], pose[e]) + Vector3.Distance(pose[e], pose[w]);
            if (length > 0.3f && length < 0.9f) _estimatedPlayerArm = Mathf.Lerp(_estimatedPlayerArm, length, 0.02f);
        }

        private void UpdateArm(PoseFrame pose, bool hasPose, float side,
            PoseJoint shoulderJoint, PoseJoint elbowJoint, PoseJoint wristJoint, PoseJoint indexJoint,
            float scale, float blend, ref Vector3 hand, ref Vector3 elbowHint,
            Transform upperArm, Transform forearm, Transform glove)
        {
            var shoulder = _torsoRotation * new Vector3(side * shoulderWidth * 0.5f, 0f, 0f);

            Vector3 targetHand;
            Vector3 targetElbow;
            if (hasPose && pose.IsVisible(wristJoint, minVisibility) && pose.IsVisible(shoulderJoint, minVisibility))
            {
                // The player's hand relative to their shoulder, scaled to the robot's arm length.
                var playerShoulder = pose[shoulderJoint];
                targetHand = shoulder + (pose[wristJoint] - playerShoulder) * scale;
                targetElbow = pose.IsVisible(elbowJoint, minVisibility)
                    ? shoulder + (pose[elbowJoint] - playerShoulder) * scale
                    : shoulder + new Vector3(side * 0.2f, -0.3f, -0.1f);
            }
            else
            {
                // Hand not visible: drift back to a boxing guard.
                targetHand = GuardPosition(side);
                targetElbow = shoulder + new Vector3(side * 0.15f, -0.3f, 0f);
                blend *= 0.25f;
            }

            hand = Vector3.Lerp(hand, targetHand, blend);
            elbowHint = Vector3.Lerp(elbowHint, targetElbow, blend);

            // Bias the bend slightly down and outwards so elbows never flip inside the body.
            var pole = elbowHint + new Vector3(side * 0.05f, -0.05f, 0f);
            var elbow = TwoBoneIK.Solve(shoulder, hand, upperArmLength, forearmLength, pole, out var reached);

            glove.localPosition = reached;
            var forearmDirection = reached - elbow;
            if (hasPose && pose.IsVisible(indexJoint, minVisibility) && pose.IsVisible(wristJoint, minVisibility))
            {
                var knuckles = pose[indexJoint] - pose[wristJoint];
                if (knuckles.sqrMagnitude > 1e-6f) forearmDirection = Vector3.Lerp(forearmDirection.normalized, knuckles.normalized, 0.5f);
            }
            if (forearmDirection.sqrMagnitude > 1e-6f)
            {
                glove.localRotation = Quaternion.LookRotation(forearmDirection.normalized, _torsoRotation * Vector3.up);
            }

            upperArm.gameObject.SetActive(showArms);
            forearm.gameObject.SetActive(showArms);
            if (showArms)
            {
                PlaceLimb(upperArm, shoulder, elbow, 0.08f);
                PlaceLimb(forearm, elbow, reached, 0.07f);
            }
        }

        private Vector3 GuardPosition(float side) => new Vector3(side * 0.16f, 0.12f, 0.3f);

        /// <summary>Stretch a capsule (2 units tall along local Y) between two points.</summary>
        private static void PlaceLimb(Transform limb, Vector3 from, Vector3 to, float thickness)
        {
            var direction = to - from;
            var length = direction.magnitude;
            if (length < 1e-4f) return;
            limb.localPosition = (from + to) * 0.5f;
            limb.localRotation = Quaternion.FromToRotation(Vector3.up, direction / length);
            limb.localScale = new Vector3(thickness, length * 0.5f, thickness);
        }
    }
}
