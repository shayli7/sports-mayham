using System.Collections;
using System.Diagnostics;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using Mediapipe.Unity;
using Mediapipe.Unity.CoordinateSystem;
using Mediapipe.Unity.Experimental;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using MpImage = Mediapipe.Image;
using MpDelegate = Mediapipe.Tasks.Core.BaseOptions.Delegate;
using MpRunningMode = Mediapipe.Tasks.Vision.Core.RunningMode;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace SportsMayhem.Tracking
{
    /// <summary>
    /// Live pose tracking: opens the camera, runs MediaPipe Pose Landmarker and emits
    /// <see cref="PoseFrame"/>s in player space. Replaces the MediaPipe sample's runner and needs
    /// no Bootstrap or sample assets: just assign the two model files.
    /// </summary>
    public sealed class MediaPipePoseSource : PoseSource
    {
        public enum ModelQuality { Lite, Full }

        [Header("Model")]
        [Tooltip("pose_landmarker_lite.bytes from the MediaPipe package (Packages/MediaPipe Unity Plugin/PackageResources/MediaPipe).")]
        [SerializeField] private TextAsset liteModel;
        [Tooltip("pose_landmarker_full.bytes from the MediaPipe package.")]
        [SerializeField] private TextAsset fullModel;
        [SerializeField] private ModelQuality quality = ModelQuality.Full;
        [Tooltip("Run the model on the GPU (Android only; the Editor always uses the CPU). Falls back to the CPU if the GPU can't start.")]
        [SerializeField] private bool useGpuOnDevice = true;

        [Header("Camera")]
        [Tooltip("Use the phone's selfie camera. Ignored when Camera Name is set.")]
        [SerializeField] private bool preferFrontCamera = true;
        [Tooltip("Optional: exact camera name to use (see the Console log for the list).")]
        [SerializeField] private string cameraName = "";
        [Tooltip("Requested camera resolution. Lower is faster; the model only needs about 256 px.")]
        [SerializeField] private Vector2Int requestedResolution = new Vector2Int(640, 480);
        [SerializeField] private int requestedFps = 30;

        [Header("Detection")]
        [Range(0f, 1f)] [SerializeField] private float minPoseDetectionConfidence = 0.5f;
        [Range(0f, 1f)] [SerializeField] private float minPosePresenceConfidence = 0.5f;
        [Range(0f, 1f)] [SerializeField] private float minTrackingConfidence = 0.5f;
        [Tooltip("The camera image is mirrored before tracking, which makes MediaPipe swap left and right. " +
                 "Leave this on. If raising your RIGHT hand moves the LEFT (blue) hand, turn it off and tell Claude.")]
        [SerializeField] private bool swapLeftRight = true;

        [Header("Debug")]
        [SerializeField] private bool showCameraPreview = true;

        public WebCamTexture CameraTexture => _webCam;
        public override string Status => _status;

        // Camera image -> MediaPipe -> callback (MediaPipe's thread) -> _pending -> Update (main thread) -> Emit.
        private readonly object _lock = new object();
        private readonly RawPose _pending = new RawPose();
        private readonly RawPose _working = new RawPose();
        private readonly PoseFrame _frame = new PoseFrame();
        private int _pendingVersion;
        private int _consumedVersion;
        private volatile int _submittedRotation;

        private readonly Stopwatch _clock = new Stopwatch();
        private long _lastTimestampMs = -1;
        private WebCamTexture _webCam;
        private PoseLandmarker _landmarker;
        private TextureFramePool _framePool;
        private bool _gpuInitializedHere;
        private string _status = "Starting...";

        private sealed class RawPose
        {
            public long timestampMs;
            public int rotation;
            public bool hasPose;
            public readonly Vector3[] normalized = new Vector3[PoseJoints.Count]; // x, y in 0..1 (y down), z unused
            public readonly Vector3[] world = new Vector3[PoseJoints.Count];      // meters, MediaPipe axes
            public readonly float[] visibility = new float[PoseJoints.Count];

            public void CopyFrom(RawPose o)
            {
                timestampMs = o.timestampMs;
                rotation = o.rotation;
                hasPose = o.hasPose;
                System.Array.Copy(o.normalized, normalized, PoseJoints.Count);
                System.Array.Copy(o.world, world, PoseJoints.Count);
                System.Array.Copy(o.visibility, visibility, PoseJoints.Count);
            }
        }

        private IEnumerator Start()
        {
            yield return RequestCameraPermission();
            if (!HasCameraPermission())
            {
                _status = "No camera permission. Allow it in the phone's app settings.";
                Debug.LogWarning($"[{nameof(MediaPipePoseSource)}] {_status}");
                yield break;
            }

            if (!StartCamera()) yield break;

            var waited = 0f;
            while (_webCam.width <= 16 && waited < 10f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (_webCam.width <= 16)
            {
                _status = "Camera did not start (is another app using it?)";
                Debug.LogError($"[{nameof(MediaPipePoseSource)}] {_status}");
                yield break;
            }

            var model = quality == ModelQuality.Full ? fullModel : liteModel;
            if (model == null)
            {
                _status = $"No {quality} model assigned on {nameof(MediaPipePoseSource)}";
                Debug.LogError($"[{nameof(MediaPipePoseSource)}] {_status}. Drag pose_landmarker_{quality.ToString().ToLower()}.bytes into the field.");
                yield break;
            }

            var delegateType = MpDelegate.CPU;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (useGpuOnDevice)
            {
                if (!GpuManager.IsInitialized)
                {
                    yield return GpuManager.Initialize();
                    _gpuInitializedHere = GpuManager.IsInitialized;
                }
                if (GpuManager.IsInitialized) delegateType = MpDelegate.GPU;
                else Debug.LogWarning($"[{nameof(MediaPipePoseSource)}] GPU could not start, using CPU.");
            }
#endif

            var options = new PoseLandmarkerOptions(
                new Mediapipe.Tasks.Core.BaseOptions(delegateType, modelAssetBuffer: model.bytes),
                runningMode: MpRunningMode.LIVE_STREAM,
                numPoses: 1,
                minPoseDetectionConfidence: minPoseDetectionConfidence,
                minPosePresenceConfidence: minPosePresenceConfidence,
                minTrackingConfidence: minTrackingConfidence,
                outputSegmentationMasks: false,
                resultCallback: OnPoseResult);

            _landmarker = PoseLandmarker.CreateFromOptions(options, delegateType == MpDelegate.GPU ? GpuManager.GpuResources : null);
            _framePool = new TextureFramePool(_webCam.width, _webCam.height, TextureFormat.RGBA32, 10);
            _clock.Restart();

            _status = $"Tracking ({quality}, {delegateType}, {_webCam.width}x{_webCam.height}, rot {_webCam.videoRotationAngle})";
            Debug.Log($"[{nameof(MediaPipePoseSource)}] {_status}");

            yield return RunLoop();
        }

        private IEnumerator RunLoop()
        {
            AsyncGPUReadbackRequest request = default;
            var waitForReadback = new WaitUntil(() => request.done);
            var waitForEndOfFrame = new WaitForEndOfFrame();
            var processingOptions = new Mediapipe.Tasks.Vision.Core.ImageProcessingOptions(rotationDegrees: 0);

            while (_landmarker != null)
            {
                if (_webCam == null || !_webCam.isPlaying || !_webCam.didUpdateThisFrame)
                {
                    yield return null;
                    continue;
                }

                if (!_framePool.TryGetTextureFrame(out var textureFrame))
                {
                    yield return waitForEndOfFrame;
                    continue;
                }

                // Always mirror the image (selfie view) so it matches what the player sees.
                var rotation = (RotationAngle)_webCam.videoRotationAngle;
                var flips = ImageTransformationOptions.Build(true, _webCam.videoVerticallyMirrored, rotation);

                request = textureFrame.ReadTextureAsync(_webCam, flips.flipHorizontally, flips.flipVertically);
                yield return waitForReadback;

                if (request.hasError || _landmarker == null)
                {
                    textureFrame.Release();
                    continue;
                }

                var image = textureFrame.BuildCPUImage();
                textureFrame.Release();

                // MediaPipe requires strictly increasing timestamps.
                var timestamp = _clock.ElapsedMilliseconds;
                if (timestamp <= _lastTimestampMs) timestamp = _lastTimestampMs + 1;
                _lastTimestampMs = timestamp;

                _submittedRotation = (int)rotation;
                _landmarker.DetectAsync(image, timestamp, processingOptions);
            }
        }

        // Called on MediaPipe's thread. Copy everything out immediately: the result is reused afterwards.
        private void OnPoseResult(PoseLandmarkerResult result, MpImage image, long timestampMs)
        {
            lock (_lock)
            {
                _pending.timestampMs = timestampMs;
                _pending.rotation = _submittedRotation;
                _pending.hasPose = result.poseLandmarks != null && result.poseLandmarks.Count > 0 &&
                                   result.poseWorldLandmarks != null && result.poseWorldLandmarks.Count > 0;

                if (_pending.hasPose)
                {
                    var normalized = result.poseLandmarks[0].landmarks;
                    var world = result.poseWorldLandmarks[0].landmarks;
                    var count = Mathf.Min(PoseJoints.Count, Mathf.Min(normalized.Count, world.Count));
                    for (var i = 0; i < count; i++)
                    {
                        var n = normalized[i];
                        var w = world[i];
                        _pending.normalized[i] = new Vector3(n.x, n.y, 0f);
                        _pending.world[i] = new Vector3(w.x, w.y, w.z);
                        _pending.visibility[i] = n.visibility ?? 1f;
                    }
                }

                _pendingVersion++;
            }

            PerformanceHud.ReportTrackingResult();
        }

        private void Update()
        {
            lock (_lock)
            {
                if (_pendingVersion == _consumedVersion) return;
                _consumedVersion = _pendingVersion;
                _working.CopyFrom(_pending);
            }

            ConvertToPlayerSpace(_working, _frame, swapLeftRight);
            Emit(_frame);
        }

        /// <summary>
        /// MediaPipe image space -> player space (see <see cref="PoseFrame"/>).
        /// Undoes the camera's rotation and, because the image was mirrored, swaps left/right labels.
        /// </summary>
        private static void ConvertToPlayerSpace(RawPose raw, PoseFrame frame, bool swapLeftRight)
        {
            frame.time = raw.timestampMs / 1000.0;
            frame.hasPose = raw.hasPose;
            if (!raw.hasPose)
            {
                frame.Clear(frame.time);
                return;
            }

            var rotation = (RotationAngle)raw.rotation;
            // The image->screen mapping is a swap/flip of axes, so its linear part also turns world x/y upright.
            var origin = ToScreen(0f, 0f, rotation);

            for (var i = 0; i < PoseJoints.Count; i++)
            {
                var source = swapLeftRight ? PoseJoints.Opposite(i) : i;
                var n = raw.normalized[source];
                var w = raw.world[source];

                frame.screen[i] = ToScreen(n.x, n.y, rotation);

                var upright = ToScreen(w.x, w.y, rotation) - origin;
                // MediaPipe z: smaller = closer to the camera. Player space z: + = towards the camera.
                frame.position[i] = new Vector3(upright.x, upright.y, -w.z);
                frame.visibility[i] = raw.visibility[source];
            }

            frame.CenterOnShoulders();
        }

        private static Vector2 ToScreen(float x, float y, RotationAngle rotation)
        {
            var p = ImageCoordinate.ImageNormalizedToLocalPoint(x, y, 0f, 0f, 1f, 0f, 1f, 1f, rotation, false);
            return new Vector2(p.x, p.y);
        }

        private bool StartCamera()
        {
            var devices = WebCamTexture.devices;
            if (devices == null || devices.Length == 0)
            {
                _status = "No camera found";
                Debug.LogError($"[{nameof(MediaPipePoseSource)}] {_status}");
                return false;
            }

            var names = new string[devices.Length];
            for (var i = 0; i < devices.Length; i++) names[i] = $"'{devices[i].name}'{(devices[i].isFrontFacing ? " (front)" : "")}";
            Debug.Log($"[{nameof(MediaPipePoseSource)}] Cameras: {string.Join(", ", names)}");

            var chosen = devices[0];
            var found = false;
            if (!string.IsNullOrEmpty(cameraName))
            {
                foreach (var d in devices)
                {
                    if (d.name == cameraName) { chosen = d; found = true; break; }
                }
                if (!found) Debug.LogWarning($"[{nameof(MediaPipePoseSource)}] Camera '{cameraName}' not found, using '{chosen.name}'.");
            }
            if (!found && preferFrontCamera)
            {
                foreach (var d in devices)
                {
                    if (d.isFrontFacing) { chosen = d; break; }
                }
            }

            _webCam = new WebCamTexture(chosen.name, requestedResolution.x, requestedResolution.y, requestedFps);
            _webCam.Play();
            _status = $"Starting camera '{chosen.name}'...";
            return true;
        }

        private static IEnumerator RequestCameraPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(Permission.Camera)) yield break;
            Permission.RequestUserPermission(Permission.Camera);
            // Wait for the system dialog to be answered (or give up after 30 s).
            var waited = 0f;
            while (!Permission.HasUserAuthorizedPermission(Permission.Camera) && waited < 30f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
#else
            yield break;
#endif
        }

        private static bool HasCameraPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Permission.HasUserAuthorizedPermission(Permission.Camera);
#else
            return true;
#endif
        }

        private void OnGUI()
        {
            if (!showCameraPreview || _webCam == null || _webCam.width <= 16) return;

            // Small mirrored camera preview in the top-right corner, for checking you're in frame.
            var height = UnityEngine.Screen.height * 0.25f;
            var width = height * _webCam.width / _webCam.height;
            var safe = UnityEngine.Screen.safeArea;
            var rect = new Rect(safe.xMax - width - 16f, (UnityEngine.Screen.height - safe.yMax) + 16f, width, height);

            var texCoords = _webCam.videoVerticallyMirrored ? new Rect(1f, 1f, -1f, -1f) : new Rect(1f, 0f, -1f, 1f);
            var previous = GUI.matrix;
            GUIUtility.RotateAroundPivot(_webCam.videoRotationAngle, rect.center);
            GUI.DrawTextureWithTexCoords(rect, _webCam, texCoords);
            GUI.matrix = previous;
        }

        private void OnDestroy()
        {
            var landmarker = _landmarker;
            _landmarker = null; // stops RunLoop
            landmarker?.Close();

            _framePool?.Dispose();
            _framePool = null;

            if (_webCam != null)
            {
                _webCam.Stop();
                Destroy(_webCam);
                _webCam = null;
            }
        }

        private void OnApplicationQuit()
        {
            if (_gpuInitializedHere) GpuManager.Shutdown();
        }
    }
}
