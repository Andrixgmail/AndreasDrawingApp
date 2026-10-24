namespace AndreasDrawingApp.Models
{
    /// <summary>
    /// A single hand landmark, in pixel coordinates of the source video frame.
    /// Z is the MediaPipe-relative depth (roughly: negative = closer to camera).
    /// </summary>
    public readonly struct HandLandmark
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public HandLandmark(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// The 21 MediaPipe hand landmark indices, named for readability.
    /// https://developers.google.com/mediapipe/solutions/vision/hand_landmarker
    /// </summary>
    public static class HandLandmarkIndex
    {
        public const int Wrist = 0;

        public const int ThumbCmc = 1;
        public const int ThumbMcp = 2;
        public const int ThumbIp = 3;
        public const int ThumbTip = 4;

        public const int IndexMcp = 5;
        public const int IndexPip = 6;
        public const int IndexDip = 7;
        public const int IndexTip = 8;

        public const int MiddleMcp = 9;
        public const int MiddlePip = 10;
        public const int MiddleDip = 11;
        public const int MiddleTip = 12;

        public const int RingMcp = 13;
        public const int RingPip = 14;
        public const int RingDip = 15;
        public const int RingTip = 16;

        public const int PinkyMcp = 17;
        public const int PinkyPip = 18;
        public const int PinkyDip = 19;
        public const int PinkyTip = 20;

        public const int Count = 21;
    }
}
