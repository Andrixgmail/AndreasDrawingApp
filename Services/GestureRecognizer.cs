using System;
using AndreasDrawingApp.Models;

namespace AndreasDrawingApp.Services
{
    /// <summary>
    /// Turns a frame of 21 hand landmarks into a GestureType, and tracks how long an
    /// open-palm pose has been held so the UI can trigger "clear canvas" after 1.5s.
    /// </summary>
    public class GestureRecognizer
    {
        public TimeSpan ClearHoldDuration { get; set; } = TimeSpan.FromSeconds(1.5);

        private DateTime? _openPalmStartedAt;

        /// <summary>0..1 progress toward triggering a clear, for UI feedback (e.g. a ring/progress bar).</summary>
        public double ClearProgress { get; private set; }

        public GestureType Classify(HandLandmark[]? landmarks)
        {
            if (landmarks is null || landmarks.Length < HandLandmarkIndex.Count)
            {
                _openPalmStartedAt = null;
                ClearProgress = 0;
                return GestureType.None;
            }

            bool indexExtended = IsExtended(landmarks, HandLandmarkIndex.IndexTip, HandLandmarkIndex.IndexPip);
            bool middleExtended = IsExtended(landmarks, HandLandmarkIndex.MiddleTip, HandLandmarkIndex.MiddlePip);
            bool ringExtended = IsExtended(landmarks, HandLandmarkIndex.RingTip, HandLandmarkIndex.RingPip);
            bool pinkyExtended = IsExtended(landmarks, HandLandmarkIndex.PinkyTip, HandLandmarkIndex.PinkyPip);

            // Open palm = all four fingers extended -> hold to clear.
            if (indexExtended && middleExtended && ringExtended && pinkyExtended)
            {
                _openPalmStartedAt ??= DateTime.UtcNow;
                var held = DateTime.UtcNow - _openPalmStartedAt.Value;
                ClearProgress = Math.Clamp(held.TotalSeconds / ClearHoldDuration.TotalSeconds, 0, 1);
                return GestureType.OpenPalm;
            }

            _openPalmStartedAt = null;
            ClearProgress = 0;

            // Index + middle extended together -> hover (move without drawing).
            if (indexExtended && middleExtended)
                return GestureType.Hover;

            // Index extended, middle down -> actively drawing.
            if (indexExtended && !middleExtended)
                return GestureType.Drawing;

            return GestureType.None;
        }

        /// <summary>True once open palm has been held continuously for ClearHoldDuration.</summary>
        public bool ShouldTriggerClear() => ClearProgress >= 1.0;

        public void ResetClearTimer()
        {
            _openPalmStartedAt = null;
            ClearProgress = 0;
        }

        private static bool IsExtended(HandLandmark[] lm, int tipIdx, int pipIdx)
        {
            // Image Y grows downward, so an extended (raised) finger has a smaller Y at the tip
            // than at its PIP joint. A small margin avoids flicker right at the threshold.
            const float margin = 4f;
            return lm[tipIdx].Y < lm[pipIdx].Y - margin;
        }
    }
}
