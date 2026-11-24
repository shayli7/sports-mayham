using System;
using UnityEngine;

namespace SportsMayhem.Tracking
{
    /// <summary>
    /// The one place gameplay gets poses from. Picks the live camera or a recorded session,
    /// smooths the pose, and publishes it.
    ///
    ///   source (camera / recording) -> RawPoseReceived -> smoothing -> PoseUpdated / Current
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class PoseTracker : MonoBehaviour
    {
        public enum Mode { Live, Playback }

        [Tooltip("Live = phone or webcam. Playback = a recorded session (set it on the Playback Pose Source).")]
        [SerializeField] private Mode mode = Mode.Live;
        [SerializeField] private MediaPipePoseSource liveSource;
        [SerializeField] private PlaybackPoseSource playbackSource;

        [Header("Smoothing")]
        [SerializeField] private bool smoothing = true;
        [SerializeField] private PoseFilter.Settings filterSettings = new PoseFilter.Settings();

        /// <summary>The latest smoothed pose. Read this from gameplay code.</summary>
        public PoseFrame Current { get; } = new PoseFrame();

        /// <summary>The latest pose exactly as the source produced it (before smoothing).</summary>
        public PoseFrame Raw { get; } = new PoseFrame();

        /// <summary>Raised with the unsmoothed pose (used by the recorder).</summary>
        public event Action<PoseFrame> RawPoseReceived;

        /// <summary>Raised with the smoothed pose, once per new tracking result.</summary>
        public event Action<PoseFrame> PoseUpdated;

        public Mode CurrentMode => mode;
        public PoseSource ActiveSource { get; private set; }

        private readonly PoseFilter _filter = new PoseFilter();

        private void Awake()
        {
            // Only the chosen source runs; the other one never starts (no camera opened during playback).
            if (liveSource != null) liveSource.enabled = mode == Mode.Live;
            if (playbackSource != null) playbackSource.enabled = mode == Mode.Playback;

            ActiveSource = mode == Mode.Live ? (PoseSource)liveSource : playbackSource;
            if (ActiveSource == null)
            {
                Debug.LogError($"[{nameof(PoseTracker)}] No {(mode == Mode.Live ? "live" : "playback")} source assigned.", this);
            }
        }

        private void OnEnable()
        {
            if (ActiveSource != null) ActiveSource.FrameProduced += HandleFrame;
        }

        private void OnDisable()
        {
            if (ActiveSource != null) ActiveSource.FrameProduced -= HandleFrame;
        }

        private void HandleFrame(PoseFrame frame)
        {
            Raw.CopyFrom(frame);
            RawPoseReceived?.Invoke(Raw);

            Current.CopyFrom(frame);
            if (smoothing) _filter.Apply(Current, filterSettings);
            else _filter.Reset();

            PoseUpdated?.Invoke(Current);
        }
    }
}
