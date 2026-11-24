using UnityEngine;

namespace SportsMayhem.Tracking
{
    /// <summary>
    /// One-Euro filter for a moving point (Casiez et al. 2012).
    /// Slow movement is smoothed heavily (no jitter); fast movement is barely smoothed (no lag on punches).
    /// </summary>
    public sealed class OneEuroFilter3
    {
        private bool _initialized;
        private Vector3 _value;
        private Vector3 _velocity;

        public void Reset() => _initialized = false;

        /// <param name="minCutoff">Hz. Lower = smoother when still, but more lag.</param>
        /// <param name="beta">How quickly smoothing drops as speed rises. Higher = less lag on fast moves.</param>
        /// <param name="derivativeCutoff">Hz. Smoothing of the speed estimate itself (1 is a good default).</param>
        public Vector3 Filter(Vector3 input, float dt, float minCutoff, float beta, float derivativeCutoff)
        {
            if (!_initialized || dt <= 0f)
            {
                _initialized = true;
                _value = input;
                _velocity = Vector3.zero;
                return input;
            }

            var rawVelocity = (input - _value) / dt;
            _velocity = Vector3.Lerp(_velocity, rawVelocity, Alpha(dt, derivativeCutoff));

            var cutoff = minCutoff + beta * _velocity.magnitude;
            _value = Vector3.Lerp(_value, input, Alpha(dt, cutoff));
            return _value;
        }

        private static float Alpha(float dt, float cutoff)
        {
            var tau = 1f / (2f * Mathf.PI * Mathf.Max(cutoff, 1e-4f));
            return 1f / (1f + tau / dt);
        }
    }
}
