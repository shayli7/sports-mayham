using System.IO;
using System.Linq;
using SportsMayhem.Tracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SportsMayhem.EditorTools
{
    /// <summary>Menu: Sports Mayhem > Create Tracking Test Scene.</summary>
    public static class TrackingSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/TrackingTest.unity";
        private const string MediaPipePackageFolder = "Packages/com.github.homuler.mediapipe";

        [MenuItem("Sports Mayhem/Create Tracking Test Scene")]
        public static void CreateScene()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Tracking Test Scene", $"{ScenePath} already exists. Replace it?", "Replace", "Cancel"))
            {
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Camera behind the player looking forward (+z): a mirror view of the stick figure.
            var camera = Camera.main;
            if (camera != null)
            {
                camera.transform.SetPositionAndRotation(new Vector3(0f, 1.3f, -2.2f), Quaternion.identity);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
            }

            var tracking = new GameObject("Tracking");
            var live = tracking.AddComponent<MediaPipePoseSource>();
            var playback = tracking.AddComponent<PlaybackPoseSource>();
            var tracker = tracking.AddComponent<PoseTracker>();
            var recorder = tracking.AddComponent<PoseRecorder>();

            var lite = FindModel("pose_landmarker_lite");
            var full = FindModel("pose_landmarker_full");
            SetReferences(live, ("liteModel", lite), ("fullModel", full));
            SetReferences(tracker, ("liveSource", live), ("playbackSource", playback));
            SetReferences(recorder, ("tracker", tracker));

            var figure = new GameObject("Pose Debug View");
            figure.transform.position = new Vector3(0f, 1.4f, 0f);
            var view = figure.AddComponent<PoseDebugView>();
            SetReferences(view, ("tracker", tracker));

            new GameObject("Performance HUD").AddComponent<PerformanceHud>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettingsFirst(ScenePath);

            if (lite == null || full == null)
            {
                EditorUtility.DisplayDialog("Tracking Test Scene",
                    "Scene created, but the pose models were not found.\n\n" +
                    "Select the 'Tracking' object and drag pose_landmarker_lite.bytes and pose_landmarker_full.bytes " +
                    "(from Packages > MediaPipe Unity Plugin) into Media Pipe Pose Source.", "OK");
            }
            else
            {
                Debug.Log($"[Sports Mayhem] Created {ScenePath} and made it the first scene in the build.");
            }
        }

        [MenuItem("Sports Mayhem/Open Pose Recordings Folder")]
        public static void OpenRecordingsFolder()
        {
            Directory.CreateDirectory(PoseRecorder.RecordingsFolder);
            EditorUtility.RevealInFinder(PoseRecorder.RecordingsFolder);
        }

        private static TextAsset FindModel(string fileName)
        {
            var folders = AssetDatabase.IsValidFolder(MediaPipePackageFolder)
                ? new[] { "Assets", MediaPipePackageFolder }
                : new[] { "Assets" };

            return AssetDatabase.FindAssets(fileName, folders)
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .Where(path => Path.GetFileName(path) == fileName + ".bytes")
                .Select(path => AssetDatabase.LoadAssetAtPath<TextAsset>(path))
                .FirstOrDefault(asset => asset != null);
        }

        private static void SetReferences(Object target, params (string field, Object value)[] references)
        {
            var serialized = new SerializedObject(target);
            foreach (var (field, value) in references)
            {
                var property = serialized.FindProperty(field);
                if (property == null)
                {
                    Debug.LogError($"[Sports Mayhem] Field '{field}' not found on {target.GetType().Name}.");
                    continue;
                }
                property.objectReferenceValue = value;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddToBuildSettingsFirst(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != scenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
