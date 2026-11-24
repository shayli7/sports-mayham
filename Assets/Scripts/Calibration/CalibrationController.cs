using System;
using SportsMayhem.Tracking;
using UnityEngine;

namespace SportsMayhem.Calibration
{
    /// <summary>
    /// Calibration flow and the tracking-lost pause.
    ///
    /// 1. Framing: stand inside the guide box (distance, centering, lighting, hands visible).
    /// 2. T-pose: hold both arms out to measure arm and shoulder size.
    /// 3. Playing: if the player leaves the frame, the game pauses (Time.timeScale = 0) until they return.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class CalibrationController : MonoBehaviour
    {
        public enum State { Framing, TPose, Playing }

        [SerializeField] private PoseTracker tracker;
        [Tooltip("Skip calibration on start when this device already has a saved calibration.")]
        [SerializeField] private bool useSavedCalibration = true;
        [SerializeField] private BodyChecks.Limits limits = new BodyChecks.Limits();

        [Header("Timing (seconds)")]
        [SerializeField] private float framingHoldTime = 1.5f;
        [SerializeField] private float tPoseHoldTime = 1.2f;
        [Tooltip("How long tracking must be lost before the game pauses (short glitches are ignored).")]
        [SerializeField] private float pauseAfter = 0.5f;
        [Tooltip("How long tracking must be good again before the game resumes.")]
        [SerializeField] private float resumeAfter = 1f;

        [Header("Pause")]
        [Tooltip("Set Time.timeScale to 0 while paused. Turn off if gameplay handles IsPaused itself.")]
        [SerializeField] private bool freezeTimeWhenPaused = true;

        public State CurrentState { get; private set; } = State.Framing;
        public bool IsPaused { get; private set; }
        public FramingIssue CurrentIssue { get; private set; } = FramingIssue.NoPerson;

        public event Action CalibrationCompleted;
        public event Action<bool> PausedChanged;

        private float _holdTimer;
        private float _issueTimer;
        private float _goodTimer;
        private float _brightness = -1f;
        private float _brightnessTimer;
        private Color32[] _pixelBuffer;

        // T-pose measurement accumulators.
        private int _samples;
        private float _sumShoulder, _sumUpper, _sumFore, _sumScreenWidth;
        private Vector2 _sumScreenCenter;

        private Texture2D _white;
        private GUIStyle _titleStyle, _textStyle, _buttonStyle;

        private void Start()
        {
            _white = Texture2D.whiteTexture;
            if (useSavedCalibration && PlayerCalibration.Current.isCalibrated) EnterPlaying();
            else BeginCalibration();
        }

        public void BeginCalibration()
        {
            SetPaused(false);
            CurrentState = State.Framing;
            _holdTimer = 0f;
        }

        private void Update()
        {
            if (tracker == null) return;
            var dt = Time.unscaledDeltaTime;
            UpdateBrightness(dt);

            var pose = tracker.Current;
            switch (CurrentState)
            {
                case State.Framing:
                    CurrentIssue = BodyChecks.CheckFraming(pose, limits, _brightness);
                    _holdTimer = CurrentIssue == FramingIssue.None ? _holdTimer + dt : 0f;
                    if (_holdTimer >= framingHoldTime)
                    {
                        CurrentState = State.TPose;
                        _holdTimer = 0f;
                        ResetSamples();
                    }
                    break;

                case State.TPose:
                    CurrentIssue = BodyChecks.CheckTracking(pose, limits, _brightness);
                    if (CurrentIssue == FramingIssue.None && BodyChecks.IsTPose(pose, limits.minVisibility))
                    {
                        _holdTimer += dt;
                        AddSample(pose);
                        if (_holdTimer >= tPoseHoldTime) FinishCalibration();
                    }
                    else
                    {
                        _holdTimer = 0f;
                        ResetSamples();
                    }
                    break;

                case State.Playing:
                    UpdatePause(pose, dt);
                    break;
            }
        }

        private void UpdatePause(PoseFrame pose, float dt)
        {
            CurrentIssue = BodyChecks.CheckTracking(pose, limits, _brightness);
            if (CurrentIssue != FramingIssue.None)
            {
                _goodTimer = 0f;
                _issueTimer += dt;
                if (!IsPaused && _issueTimer >= pauseAfter) SetPaused(true);
            }
            else
            {
                _issueTimer = 0f;
                _goodTimer += dt;
                if (IsPaused && _goodTimer >= resumeAfter) SetPaused(false);
            }
        }

        private void SetPaused(bool paused)
        {
            if (IsPaused == paused) return;
            IsPaused = paused;
            if (freezeTimeWhenPaused) Time.timeScale = paused ? 0f : 1f;
            PausedChanged?.Invoke(paused);
        }

        private void ResetSamples()
        {
            _samples = 0;
            _sumShoulder = _sumUpper = _sumFore = _sumScreenWidth = 0f;
            _sumScreenCenter = Vector2.zero;
        }

        private void AddSample(PoseFrame pose)
        {
            _samples++;
            _sumShoulder += pose.shoulderWidth;
            _sumUpper += 0.5f * (Vector3.Distance(pose[PoseJoint.LeftShoulder], pose[PoseJoint.LeftElbow]) +
                                 Vector3.Distance(pose[PoseJoint.RightShoulder], pose[PoseJoint.RightElbow]));
            _sumFore += 0.5f * (Vector3.Distance(pose[PoseJoint.LeftElbow], pose[PoseJoint.LeftWrist]) +
                                Vector3.Distance(pose[PoseJoint.RightElbow], pose[PoseJoint.RightWrist]));
            _sumScreenWidth += BodyChecks.ScreenShoulderWidth(pose);
            _sumScreenCenter += BodyChecks.ShoulderCenter(pose);
        }

        private void FinishCalibration()
        {
            if (_samples == 0) return;
            var n = (float)_samples;
            var calibration = new PlayerCalibration
            {
                isCalibrated = true,
                shoulderWidth = _sumShoulder / n,
                upperArmLength = _sumUpper / n,
                forearmLength = _sumFore / n,
                screenShoulderWidth = _sumScreenWidth / n,
                screenCenter = _sumScreenCenter / n,
            };
            PlayerCalibration.Save(calibration);
            Debug.Log($"[Calibration] shoulders {calibration.shoulderWidth:F2} m, upper arm {calibration.upperArmLength:F2} m, " +
                      $"forearm {calibration.forearmLength:F2} m ({_samples} samples)");

            EnterPlaying();
            CalibrationCompleted?.Invoke();
        }

        private void EnterPlaying()
        {
            CurrentState = State.Playing;
            _issueTimer = _goodTimer = 0f;
            SetPaused(false);
        }

        private void UpdateBrightness(float dt)
        {
            var camera = tracker.LiveSource != null ? tracker.LiveSource.CameraTexture : null;
            if (camera == null || camera.width <= 16)
            {
                _brightness = -1f;
                return;
            }

            _brightnessTimer -= dt;
            if (_brightnessTimer > 0f) return;
            _brightnessTimer = 0.5f;

            var count = camera.width * camera.height;
            if (_pixelBuffer == null || _pixelBuffer.Length != count) _pixelBuffer = new Color32[count];
            camera.GetPixels32(_pixelBuffer);

            long sum = 0;
            var sampled = 0;
            for (var i = 0; i < count; i += 97) // a sparse sample is plenty for an average
            {
                var c = _pixelBuffer[i];
                sum += (c.r * 299 + c.g * 587 + c.b * 114) / 1000;
                sampled++;
            }
            _brightness = sampled > 0 ? sum / (255f * sampled) : -1f;
        }

        private void OnDisable()
        {
            if (IsPaused && freezeTimeWhenPaused) Time.timeScale = 1f;
        }

        // ---------- UI ----------

        private void OnGUI()
        {
            if (tracker == null) return;
            EnsureStyles();

            switch (CurrentState)
            {
                case State.Framing:
                    DrawGuideBox(CurrentIssue == FramingIssue.None ? Color.green : Color.white);
                    DrawBodyDots(tracker.Current);
                    DrawPanel("Step 1 of 2: Get in position", BodyChecks.Describe(CurrentIssue), _holdTimer / framingHoldTime);
                    DrawSkipButton();
                    break;

                case State.TPose:
                    DrawBodyDots(tracker.Current);
                    var hint = CurrentIssue != FramingIssue.None
                        ? BodyChecks.Describe(CurrentIssue)
                        : "Hold both arms straight out to the sides, at shoulder height";
                    DrawPanel("Step 2 of 2: Make a T", hint, _holdTimer / tPoseHoldTime);
                    DrawSkipButton();
                    break;

                case State.Playing:
                    if (IsPaused)
                    {
                        DrawPanel("Paused", BodyChecks.Describe(CurrentIssue), _goodTimer / resumeAfter);
                    }
                    else
                    {
                        var size = _buttonStyle.fontSize;
                        var rect = new Rect((UnityEngine.Screen.width - size * 8f) * 0.5f, 16f, size * 8f, size * 1.8f);
                        if (GUI.Button(rect, "Recalibrate", _buttonStyle)) BeginCalibration();
                    }
                    break;
            }
        }

        private void EnsureStyles()
        {
            var size = Mathf.Max(16, Mathf.RoundToInt(UnityEngine.Screen.height * 0.04f));
            _titleStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
            _textStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            _buttonStyle ??= new GUIStyle(GUI.skin.button);
            _titleStyle.fontSize = size;
            _textStyle.fontSize = Mathf.RoundToInt(size * 0.8f);
            _buttonStyle.fontSize = Mathf.RoundToInt(size * 0.6f);
        }

        private void DrawPanel(string title, string text, float progress)
        {
            var w = UnityEngine.Screen.width * 0.6f;
            var h = _titleStyle.fontSize * 4.2f;
            var rect = new Rect((UnityEngine.Screen.width - w) * 0.5f, UnityEngine.Screen.height * 0.22f, w, h);

            DrawRect(rect, new Color(0f, 0f, 0f, 0.65f));
            GUI.Label(new Rect(rect.x, rect.y + h * 0.05f, w, h * 0.35f), title, _titleStyle);
            GUI.Label(new Rect(rect.x + 16f, rect.y + h * 0.38f, w - 32f, h * 0.4f), text, _textStyle);

            var bar = new Rect(rect.x + 24f, rect.yMax - h * 0.14f, w - 48f, h * 0.06f);
            DrawRect(bar, new Color(1f, 1f, 1f, 0.2f));
            bar.width *= Mathf.Clamp01(progress);
            DrawRect(bar, new Color(0.3f, 1f, 0.4f, 0.9f));
        }

        private void DrawSkipButton()
        {
            var size = _buttonStyle.fontSize;
            var rect = new Rect(UnityEngine.Screen.width - size * 7f - 16f, UnityEngine.Screen.height - size * 2.2f - 16f, size * 7f, size * 2.2f);
            if (GUI.Button(rect, PlayerCalibration.Current.isCalibrated ? "Use saved" : "Skip", _buttonStyle)) EnterPlaying();
        }

        /// <summary>The zone the shoulder midpoint should be in, with marks at the target shoulder width.</summary>
        private void DrawGuideBox(Color color)
        {
            var min = ToGui(new Vector2(limits.minCenterX, limits.maxCenterY));
            var max = ToGui(new Vector2(limits.maxCenterX, limits.minCenterY));
            var box = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            const float t = 4f;
            DrawRect(new Rect(box.x, box.y, box.width, t), color);
            DrawRect(new Rect(box.x, box.yMax - t, box.width, t), color);
            DrawRect(new Rect(box.x, box.y, t, box.height), color);
            DrawRect(new Rect(box.xMax - t, box.y, t, box.height), color);
        }

        private void DrawBodyDots(PoseFrame pose)
        {
            if (!pose.hasPose) return;
            DrawDot(pose, PoseJoint.Nose, Color.white);
            DrawDot(pose, PoseJoint.LeftShoulder, new Color(0.25f, 0.55f, 1f));
            DrawDot(pose, PoseJoint.LeftWrist, new Color(0.25f, 0.55f, 1f));
            DrawDot(pose, PoseJoint.RightShoulder, new Color(1f, 0.3f, 0.3f));
            DrawDot(pose, PoseJoint.RightWrist, new Color(1f, 0.3f, 0.3f));
            var center = ToGui(BodyChecks.ShoulderCenter(pose));
            DrawRect(new Rect(center.x - 6f, center.y - 6f, 12f, 12f), Color.yellow);
        }

        private void DrawDot(PoseFrame pose, PoseJoint joint, Color color)
        {
            if (!pose.IsVisible(joint, limits.minVisibility)) return;
            var p = ToGui(pose.ScreenPoint(joint));
            var r = Mathf.Max(8f, UnityEngine.Screen.height * 0.015f);
            DrawRect(new Rect(p.x - r, p.y - r, r * 2f, r * 2f), color);
        }

        /// <summary>Pose screen coordinates (0..1, y up) to GUI pixels (y down).</summary>
        private static Vector2 ToGui(Vector2 screen) =>
            new Vector2(screen.x * UnityEngine.Screen.width, (1f - screen.y) * UnityEngine.Screen.height);

        private void DrawRect(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _white);
            GUI.color = previous;
        }
    }
}
