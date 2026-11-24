using System;
using System.IO;
using UnityEngine;

namespace SportsMayhem.Tracking
{
    /// <summary>
    /// Records live poses to a file, with an on-screen REC button.
    ///
    /// Files go to Application.persistentDataPath/PoseRecordings:
    /// - Phone: /sdcard/Android/data/(package name)/files/PoseRecordings (copy with "adb pull").
    /// - Editor: menu Sports Mayhem > Open Pose Recordings Folder.
    /// Drop a file anywhere under Assets to play it back with <see cref="PlaybackPoseSource"/>.
    /// </summary>
    public sealed class PoseRecorder : MonoBehaviour
    {
        [SerializeField] private PoseTracker tracker;
        [SerializeField] private bool showButton = true;

        public bool IsRecording => _writer != null;
        public string LastSavedPath { get; private set; }

        public static string RecordingsFolder => Path.Combine(Application.persistentDataPath, "PoseRecordings");

        private PoseRecording.Writer _writer;
        private string _currentPath;
        private GUIStyle _buttonStyle;
        private GUIStyle _labelStyle;

        private void OnEnable()
        {
            if (tracker != null) tracker.RawPoseReceived += HandleRawPose;
        }

        private void OnDisable()
        {
            if (tracker != null) tracker.RawPoseReceived -= HandleRawPose;
            StopRecording();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) StopRecording(); // the OS may kill a paused app; make sure the file is complete
        }

        public void StartRecording()
        {
            if (IsRecording) return;
            if (tracker != null && tracker.CurrentMode == PoseTracker.Mode.Playback)
            {
                Debug.LogWarning($"[{nameof(PoseRecorder)}] Switch the Pose Tracker to Live to record.");
                return;
            }

            Directory.CreateDirectory(RecordingsFolder);
            _currentPath = Path.Combine(RecordingsFolder, $"pose_{DateTime.Now:yyyyMMdd_HHmmss}{PoseRecording.FileExtension}");
            _writer = new PoseRecording.Writer(_currentPath);
            Debug.Log($"[{nameof(PoseRecorder)}] Recording to {_currentPath}");
        }

        public void StopRecording()
        {
            if (!IsRecording) return;

            var frames = _writer.FrameCount;
            var seconds = _writer.Duration;
            _writer.Dispose();
            _writer = null;
            LastSavedPath = _currentPath;
            Debug.Log($"[{nameof(PoseRecorder)}] Saved {frames} frames ({seconds:F1} s) to {LastSavedPath}");
        }

        private void HandleRawPose(PoseFrame frame)
        {
            _writer?.Write(frame);
        }

        private void OnGUI()
        {
            if (!showButton) return;

            var size = Mathf.Max(14, Mathf.RoundToInt(UnityEngine.Screen.height * 0.03f));
            _buttonStyle ??= new GUIStyle(GUI.skin.button);
            _labelStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
            _buttonStyle.fontSize = size;
            _labelStyle.fontSize = Mathf.RoundToInt(size * 0.6f);

            var safe = UnityEngine.Screen.safeArea;
            var height = size * 2.2f;
            var rect = new Rect(safe.x + 16f, UnityEngine.Screen.height - safe.y - height - 16f, size * 9f, height);

            var label = IsRecording ? $"■ STOP  {_writer.Duration:F1} s" : "● REC";
            var previousColor = GUI.backgroundColor;
            if (IsRecording) GUI.backgroundColor = Color.red;
            if (GUI.Button(rect, label, _buttonStyle))
            {
                if (IsRecording) StopRecording();
                else StartRecording();
            }
            GUI.backgroundColor = previousColor;

            if (!IsRecording && LastSavedPath != null)
            {
                var info = new Rect(rect.x, rect.y - size * 2.5f, UnityEngine.Screen.width * 0.6f, size * 2.4f);
                GUI.Label(info, $"Saved: {LastSavedPath}", _labelStyle);
            }
        }
    }
}
