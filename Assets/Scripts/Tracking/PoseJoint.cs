namespace SportsMayhem.Tracking
{
    /// <summary>
    /// The 33 body points MediaPipe Pose tracks, in MediaPipe's order.
    /// "Left" and "Right" are always the PLAYER's own left and right.
    /// </summary>
    public enum PoseJoint
    {
        Nose = 0,
        LeftEyeInner = 1,
        LeftEye = 2,
        LeftEyeOuter = 3,
        RightEyeInner = 4,
        RightEye = 5,
        RightEyeOuter = 6,
        LeftEar = 7,
        RightEar = 8,
        MouthLeft = 9,
        MouthRight = 10,
        LeftShoulder = 11,
        RightShoulder = 12,
        LeftElbow = 13,
        RightElbow = 14,
        LeftWrist = 15,
        RightWrist = 16,
        LeftPinky = 17,
        RightPinky = 18,
        LeftIndex = 19,
        RightIndex = 20,
        LeftThumb = 21,
        RightThumb = 22,
        LeftHip = 23,
        RightHip = 24,
        LeftKnee = 25,
        RightKnee = 26,
        LeftAnkle = 27,
        RightAnkle = 28,
        LeftHeel = 29,
        RightHeel = 30,
        LeftFootIndex = 31,
        RightFootIndex = 32,
    }

    public static class PoseJoints
    {
        public const int Count = 33;

        /// <summary>Returns the same joint on the other side of the body (Nose stays Nose).</summary>
        public static int Opposite(int index)
        {
            if (index <= 0) return index;
            if (index <= 6) return index <= 3 ? index + 3 : index - 3; // eyes: 1-3 <-> 4-6
            // From 7 on, joints come in left/right pairs: (7,8), (9,10), (11,12) ... (31,32).
            return index % 2 == 1 ? index + 1 : index - 1;
        }

        public static PoseJoint Opposite(PoseJoint joint) => (PoseJoint)Opposite((int)joint);

        public static bool IsLeft(PoseJoint joint)
        {
            var i = (int)joint;
            if (i == 0) return false;
            if (i <= 6) return i <= 3;
            return i % 2 == 1;
        }

        public static bool IsRight(PoseJoint joint) => joint != PoseJoint.Nose && !IsLeft(joint);
    }
}
