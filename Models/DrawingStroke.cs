using System.Collections.Generic;
using SkiaSharp;

namespace AndreasDrawingApp.Models
{
    /// <summary>
    /// One continuous pen-down-to-pen-up path drawn by the user's fingertip.
    /// </summary>
    public class DrawingStroke
    {
        public List<SKPoint> Points { get; } = new();
        public SKColor Color { get; set; }
        public float Thickness { get; set; }
        public bool IsEraser { get; set; }

        public DrawingStroke(SKColor color, float thickness, bool isEraser)
        {
            Color = color;
            Thickness = thickness;
            IsEraser = isEraser;
        }
    }
}
