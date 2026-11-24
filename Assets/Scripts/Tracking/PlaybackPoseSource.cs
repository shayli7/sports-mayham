using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SportsMayhem.Tracking
{
    /// <summary>
    /// Replays a recorded session as if the camera were live, so tracking and gestures can be
    /// tested in the Editor without standing in front of a camera.
    /// </summary>
    public sealed class PlaybackPoseSource : PoseSource
    {
        [Tooltip("A recording (.bytes) made with the REC button. Put the file anywhere under Assets.")]
        [SerializeField] private TextAsset recording;
        [SerializeField] private bool loop = true;
        [Range(0.1f, 2f)] [SerializeField] private float speed = 1f;

        public override string Status => _status;

        private readonly PoseFrame _output = new PoseFrame();
        private List<PoseFrame> _frames;
        private int _nextIndex;
        private double _playTime;
        private double _loopOffset; // keeps emitted times increasing across loops
        private string _status = "No recording";

        private void Start()
        {
            if (recording == null)
            {
                _status = "No recording assigned";
                Debug.LogError($"[{nameof(PlaybackPoseSource)}] Assign a recording (.bytes) in the Inspector.", this);
                enabled = false;
                return;
            }

            try
            {
                _frames = PoseRecording.Read(recording.bytes);
            }
            catch (InvalidDataException e)
            {
                _status = $"Can't read '{recording.name}': {e.Message}";
                Debug.LogError($"[{nameof(PlaybackPoseSource)}] {_status}", this);
                enabled = false;
                return;
            }

            if (_frames.Count == 0)
            {
                _status = $"'{recording.name}' has no frames";
                Debug.LogError($"[{nameof(PlaybackPoseSource)}] {_status}", this);
                enabled = false;
            }
        }

        private void Update()
        {
            if (_frames == null || _frames.Count == 0) return;

            _playTime += Time.unscaledDeltaTime * speed; // unscaled: keeps playing while the game is paused
            var duration = _frames[_frames.Count - 1].time;

            // Emit every frame that is due, in order, so smoothing sees the original timing.
            while (_nextIndex < _frames.Count && _frames[_nextIndex].time <= _playTime)
            {
                _output.CopyFrom(_frames[_nextIndex]);
                _output.time += _loopOffset;
                Emit(_output);
                _nextIndex++;
            }

            if (_nextIndex >= _frames.Count)
            {
                if (loop)
                {
                    _loopOffset += duration + 1.0 / 30.0;
                    _playTime = 0.0;
                    _nextIndex = 0;
                }
                else
                {
                    _status = $"Finished '{recording.name}'";
                    return;
                }
            }

            _status = $"Playback '{recording.name}' {_playTime:F1} / {duration:F1} s";
        }
    }
}
