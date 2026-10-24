using System;
using SkiaSharp;
using AndreasDrawingApp.Models;

namespace AndreasDrawingApp.Services
{
    /// <summary>
    /// Detects a thumb and index finger pinch from the 21 hand landmarks.
    /// The distance between the two fingertips is divided by the palm length (wrist to
    /// middle finger knuckle), so it works the same whether the hand is near or far.
    /// Two thresholds (hysteresis) stop the pinch from flickering on and off at the boundary.
    /// If pinching feels too hard or too easy, tweak CloseRatio and OpenRatio.
    /// </summary>
    public class PinchDetector
    {
        /// <summary>Pinch starts when fingertips get closer than this fraction of the palm length.</summary>
        public float CloseRatio { get; set; } = 0.30f;

        /// <summary>Pinch ends when fingertips get farther apart than this fraction of the palm length.</summary>
        public float OpenRatio { get; set; } = 0.50f;

        public bool IsPinching { get; private set; }

        /// <summary>Midpoint between thumb tip and index tip, in camera frame pixels.</summary>
        public SKPoint Cursor { get; private set; }

        public float LastRatio { get; private set; }

        public void Update(HandLandmark[] landmarks)
        {
            var thumb = landmarks[HandLandmarkIndex.ThumbTip];
            var index = landmarks[HandLandmarkIndex.IndexTip];
            var wrist = landmarks[HandLandmarkIndex.Wrist];
            var knuckle = landmarks[HandLandmarkIndex.MiddleMcp];

            float palm = Math.Max(1f, Distance(wrist, knuckle));
            float ratio = Distance(thumb, index) / palm;
            LastRatio = ratio;

            if (IsPinching)
            {
                if (ratio > OpenRatio) IsPinching = false;
            }
            else if (ratio < CloseRatio)
            {
                IsPinching = true;
            }

            Cursor = new SKPoint((thumb.X + index.X) / 2f, (thumb.Y + index.Y) / 2f);
        }

        public void Reset()
        {
            IsPinching = false;
        }

        private static float Distance(HandLandmark a, HandLandmark b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }
    }
}
