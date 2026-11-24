using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace SportsMayhem.Tracking
{
    /// <summary>
    /// Pose recording file format (".bytes", so Unity imports it as a TextAsset when placed under Assets).
    ///
    ///   header: "SMPR" (4 bytes), int version, int jointCount
    ///   frames until end of file:
    ///     double time (seconds from the first frame), byte hasPose, float shoulderWidth,
    ///     jointCount x (position xyz, screen xy, visibility) as floats
    /// </summary>
    public static class PoseRecording
    {
        public const string FileExtension = ".bytes";
        private const string Magic = "SMPR";
        private const int Version = 1;

        public sealed class Writer : IDisposable
        {
            private readonly BinaryWriter _writer;
            private double _startTime = double.NaN;

            public int FrameCount { get; private set; }
            public double Duration { get; private set; }

            public Writer(string path)
            {
                _writer = new BinaryWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 64 * 1024));
                _writer.Write(Encoding.ASCII.GetBytes(Magic));
                _writer.Write(Version);
                _writer.Write(PoseFrame.JointCount);
            }

            public void Write(PoseFrame frame)
            {
                if (double.IsNaN(_startTime)) _startTime = frame.time;
                Duration = frame.time - _startTime;

                _writer.Write(Duration);
                _writer.Write(frame.hasPose ? (byte)1 : (byte)0);
                _writer.Write(frame.shoulderWidth);
                for (var i = 0; i < PoseFrame.JointCount; i++)
                {
                    var p = frame.position[i];
                    var s = frame.screen[i];
                    _writer.Write(p.x);
                    _writer.Write(p.y);
                    _writer.Write(p.z);
                    _writer.Write(s.x);
                    _writer.Write(s.y);
                    _writer.Write(frame.visibility[i]);
                }
                FrameCount++;
            }

            public void Dispose() => _writer.Dispose();
        }

        /// <summary>Reads a whole recording. Throws <see cref="InvalidDataException"/> for files that aren't recordings.</summary>
        public static List<PoseFrame> Read(byte[] data)
        {
            var frames = new List<PoseFrame>();
            using var reader = new BinaryReader(new MemoryStream(data));

            if (data.Length < 12 || Encoding.ASCII.GetString(reader.ReadBytes(4)) != Magic)
            {
                throw new InvalidDataException("Not a Sports Mayhem pose recording.");
            }

            var version = reader.ReadInt32();
            if (version != Version)
            {
                throw new InvalidDataException($"Unsupported recording version {version}.");
            }

            var jointCount = reader.ReadInt32();
            var frameSize = 8 + 1 + 4 + jointCount * 6 * 4;

            while (reader.BaseStream.Length - reader.BaseStream.Position >= frameSize)
            {
                var frame = new PoseFrame
                {
                    time = reader.ReadDouble(),
                    hasPose = reader.ReadByte() != 0,
                    shoulderWidth = reader.ReadSingle(),
                };
                for (var i = 0; i < jointCount; i++)
                {
                    var p = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    var s = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                    var v = reader.ReadSingle();
                    if (i >= PoseFrame.JointCount) continue;
                    frame.position[i] = p;
                    frame.screen[i] = s;
                    frame.visibility[i] = v;
                }
                frames.Add(frame);
            }

            return frames;
        }
    }
}
