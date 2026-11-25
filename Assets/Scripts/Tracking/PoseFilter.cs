using System;
using UnityEngine;

namespace SportsMayhem.Tracking
{
    /// <summary>Smooths every joint of a <see cref="PoseFrame"/> with a One-Euro filter.</summary>
    public sealed class PoseFilter
    {
        [Serializable]
        public class Settings
        {
            [Tooltip("Hz. Lower = steadier when still, but laggier. Try 0.5 to 3.")]
            public float minCutoff = 1.5f;

            [Tooltip("Higher = less lag on fast moves (punches), but more jitter. Try 4 to 20.")]
            public float beta = 10f;

            [Tooltip("Hz. How quickly the filter notices a move has started. Higher = punches respond sooner.")]
            public float derivativeCutoff = 4f;
        }

        // Gaps longer than this (tracking lost, playback restarted) reset the filter instead of smoothing across them.
        private const float MaxGapSeconds = 0.5f;

        private readonly OneEuroFilter3[] _position = new OneEuroFilter3[PoseFrame.JointCount];
        private readonly OneEuroFilter3[] _screen = new OneEuroFilter3[PoseFrame.JointCount];
        private double _lastTime;
        private bool _hasLast;

        public PoseFilter()
        {
            for (var i = 0; i < PoseFrame.JointCount; i++)
            {
                _position[i] = new OneEuroFilter3();
                _screen[i] = new OneEuroFilter3();
            }
        }

        public void Reset()
        {
            _hasLast = false;
            for (var i = 0; i < PoseFrame.JointCount; i++)
            {
                _position[i].Reset();
                _screen[i].Reset();
            }
        }

        /// <summary>Smooths <paramref name="frame"/> in place.</summary>
        public void Apply(PoseFrame frame, Settings settings)
        {
            if (!frame.hasPose)
            {
                Reset();
                return;
            }

            var dt = _hasLast ? (float)(frame.time - _lastTime) : 0f;
            if (_hasLast && (dt <= 0f || dt > MaxGapSeconds))
            {
                Reset();
                dt = 0f;
            }

            for (var i = 0; i < PoseFrame.JointCount; i++)
            {
                frame.position[i] = _position[i].Filter(frame.position[i], dt, settings.minCutoff, settings.beta, settings.derivativeCutoff);
                frame.screen[i] = _screen[i].Filter(frame.screen[i], dt, settings.minCutoff, settings.beta, settings.derivativeCutoff);
            }

            _lastTime = frame.time;
            _hasLast = true;
        }
    }
}
