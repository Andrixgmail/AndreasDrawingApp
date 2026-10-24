using SkiaSharp;

namespace AndreasDrawingApp.Services
{
    /// <summary>
    /// Exponential Moving Average smoother for a 2D point stream.
    /// smoothed_t = alpha * raw_t + (1 - alpha) * smoothed_(t-1)
    /// Lower alpha = smoother but laggier. 0.35-0.5 feels responsive but jitter-free
    /// for a webcam-driven cursor.
    /// </summary>
    public class EmaSmoother
    {
        private readonly float _alpha;
        private SKPoint? _current;

        public EmaSmoother(float alpha = 0.4f)
        {
            _alpha = alpha;
        }

        public SKPoint Smooth(SKPoint raw)
        {
            if (_current is null)
            {
                _current = raw;
                return raw;
            }

            var prev = _current.Value;
            var next = new SKPoint(
                _alpha * raw.X + (1 - _alpha) * prev.X,
                _alpha * raw.Y + (1 - _alpha) * prev.Y);

            _current = next;
            return next;
        }

        /// <summary>Call when a stroke ends so the next stroke doesn't inherit stale momentum.</summary>
        public void Reset() => _current = null;
    }
}
