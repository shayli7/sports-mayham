using UnityEngine;

namespace SportsMayhem.Tracking
{
    /// <summary>
    /// Debug stick figure of the upper body, built from primitives at runtime.
    /// The player's LEFT side is blue, RIGHT side is red. Place this object where the
    /// shoulder midpoint should be, and look at it from behind (camera at negative z, facing +z).
    /// </summary>
    public sealed class PoseDebugView : MonoBehaviour
    {
        [SerializeField] private PoseTracker tracker;
        [Tooltip("Show the unsmoothed pose instead (to compare smoothing on/off).")]
        [SerializeField] private bool showRaw;
        [SerializeField] private float scale = 1f;
        [Range(0f, 1f)] [SerializeField] private float minVisibility = 0.3f;
        [SerializeField] private bool showStatus = true;

        private static readonly PoseJoint[] Joints =
        {
            PoseJoint.Nose, PoseJoint.LeftEar, PoseJoint.RightEar,
            PoseJoint.LeftShoulder, PoseJoint.RightShoulder,
            PoseJoint.LeftElbow, PoseJoint.RightElbow,
            PoseJoint.LeftWrist, PoseJoint.RightWrist,
            PoseJoint.LeftIndex, PoseJoint.RightIndex,
            PoseJoint.LeftHip, PoseJoint.RightHip,
        };

        private static readonly PoseJoint[,] Bones =
        {
            { PoseJoint.LeftShoulder, PoseJoint.RightShoulder },
            { PoseJoint.LeftShoulder, PoseJoint.LeftElbow },
            { PoseJoint.LeftElbow, PoseJoint.LeftWrist },
            { PoseJoint.LeftWrist, PoseJoint.LeftIndex },
            { PoseJoint.RightShoulder, PoseJoint.RightElbow },
            { PoseJoint.RightElbow, PoseJoint.RightWrist },
            { PoseJoint.RightWrist, PoseJoint.RightIndex },
            { PoseJoint.LeftShoulder, PoseJoint.LeftHip },
            { PoseJoint.RightShoulder, PoseJoint.RightHip },
            { PoseJoint.LeftHip, PoseJoint.RightHip },
            { PoseJoint.LeftEar, PoseJoint.RightEar },
        };

        private static readonly Color LeftColor = new Color(0.25f, 0.55f, 1f);
        private static readonly Color RightColor = new Color(1f, 0.3f, 0.3f);
        private static readonly Color CenterColor = new Color(0.9f, 0.9f, 0.9f);

        private Transform[] _jointObjects;
        private Transform[] _boneObjects;
        private GUIStyle _style;

        private void Awake()
        {
            _jointObjects = new Transform[Joints.Length];
            for (var i = 0; i < Joints.Length; i++)
            {
                var size = Joints[i] == PoseJoint.Nose ? 0.16f : 0.06f;
                _jointObjects[i] = CreatePart(PrimitiveType.Sphere, Joints[i].ToString(), ColorFor(Joints[i]), size);
            }

            _boneObjects = new Transform[Bones.GetLength(0)];
            for (var i = 0; i < _boneObjects.Length; i++)
            {
                var a = Bones[i, 0];
                var b = Bones[i, 1];
                var color = PoseJoints.IsLeft(a) && PoseJoints.IsLeft(b) ? LeftColor
                    : PoseJoints.IsRight(a) && PoseJoints.IsRight(b) ? RightColor
                    : CenterColor;
                _boneObjects[i] = CreatePart(PrimitiveType.Cylinder, $"{a}-{b}", color, 0.03f);
            }
        }

        private Transform CreatePart(PrimitiveType type, string partName, Color color, float size)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = partName;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * size;
            go.GetComponent<Renderer>().material.color = color;
            go.SetActive(false);
            return go.transform;
        }

        private static Color ColorFor(PoseJoint joint) =>
            PoseJoints.IsLeft(joint) ? LeftColor : PoseJoints.IsRight(joint) ? RightColor : CenterColor;

        private void LateUpdate()
        {
            if (tracker == null) return;
            var pose = showRaw ? tracker.Raw : tracker.Current;

            for (var i = 0; i < Joints.Length; i++)
            {
                var visible = pose.IsVisible(Joints[i], minVisibility);
                _jointObjects[i].gameObject.SetActive(visible);
                if (visible) _jointObjects[i].localPosition = pose[Joints[i]] * scale;
            }

            for (var i = 0; i < _boneObjects.Length; i++)
            {
                var a = Bones[i, 0];
                var b = Bones[i, 1];
                var visible = pose.IsVisible(a, minVisibility) && pose.IsVisible(b, minVisibility);
                var bone = _boneObjects[i];
                bone.gameObject.SetActive(visible);
                if (!visible) continue;

                var from = pose[a] * scale;
                var to = pose[b] * scale;
                var direction = to - from;
                var length = direction.magnitude;
                if (length < 1e-4f)
                {
                    bone.gameObject.SetActive(false);
                    continue;
                }

                // A Unity cylinder is 2 units tall along its local Y axis.
                bone.localPosition = (from + to) * 0.5f;
                bone.localRotation = Quaternion.FromToRotation(Vector3.up, direction / length);
                bone.localScale = new Vector3(0.03f, length * 0.5f, 0.03f);
            }
        }

        private void OnGUI()
        {
            if (!showStatus || tracker == null) return;

            _style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, richText = true };
            _style.fontSize = Mathf.Max(14, Mathf.RoundToInt(UnityEngine.Screen.height * 0.025f));

            var source = tracker.ActiveSource != null ? tracker.ActiveSource.Status : "No source";
            var pose = tracker.Current;
            var line = pose.hasPose
                ? "Raise your <color=#FF5050>RIGHT</color> hand: the <color=#FF5050>red</color> hand should rise on the right."
                : "<color=#FFCC00>No person found. Step into view.</color>";

            var width = UnityEngine.Screen.width * 0.6f;
            var rect = new Rect((UnityEngine.Screen.width - width) * 0.5f, UnityEngine.Screen.height - _style.fontSize * 4f, width, _style.fontSize * 3.5f);
            GUI.Label(rect, $"{source}\n{line}", _style);
        }
    }
}
