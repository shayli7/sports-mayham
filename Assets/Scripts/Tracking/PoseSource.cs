using System;
using UnityEngine;

namespace SportsMayhem.Tracking
{
    /// <summary>
    /// Anything that produces poses: the live camera, a recorded session, and later keyboard input
    /// or a remote player. Frames are always delivered on Unity's main thread.
    /// </summary>
    public abstract class PoseSource : MonoBehaviour
    {
        /// <summary>Raised on the main thread for every new pose (including "no person found" frames).</summary>
        public event Action<PoseFrame> FrameProduced;

        /// <summary>Short human-readable state for debug UI, e.g. "Camera running (front)".</summary>
        public abstract string Status { get; }

        protected void Emit(PoseFrame frame) => FrameProduced?.Invoke(frame);
    }
}
