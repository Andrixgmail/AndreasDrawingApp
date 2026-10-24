using System.Collections.Generic;
using SkiaSharp;
using AndreasDrawingApp.Models;

namespace AndreasDrawingApp.Services
{
    /// <summary>
    /// Owns the list of drawn strokes and knows how to render them onto any SKCanvas
    /// (used both for the live on-screen overlay and for PNG/PDF export).
    /// </summary>
    public class DrawingCanvasManager
    {
        private readonly List<DrawingStroke> _strokes = new();
        private DrawingStroke? _activeStroke;

        public IReadOnlyList<DrawingStroke> Strokes => _strokes;

        public SKColor StrokeColor { get; set; } = SKColors.Red;
        public float StrokeThickness { get; set; } = 6f;
        public bool EraserMode { get; set; }

        public void BeginStroke(SKPoint start)
        {
            _activeStroke = new DrawingStroke(StrokeColor, StrokeThickness, EraserMode);
            _activeStroke.Points.Add(start);
            _strokes.Add(_activeStroke);
        }

        public void ExtendStroke(SKPoint point)
        {
            if (_activeStroke is null)
            {
                BeginStroke(point);
                return;
            }
            _activeStroke.Points.Add(point);
        }

        public void EndStroke()
        {
            _activeStroke = null;
        }

        public void Clear()
        {
            _strokes.Clear();
            _activeStroke = null;
        }

        /// <summary>
        /// Draws every stroke onto the given canvas. Pass a solid background color when
        /// exporting to PNG/PDF (transparent is fine for the live on-screen overlay).
        /// </summary>
        public void Render(SKCanvas canvas, SKColor? background = null)
        {
            if (background is { } bg)
                canvas.Clear(bg);
            else
                canvas.Clear(SKColors.Transparent);

            foreach (var stroke in _strokes)
            {
                if (stroke.Points.Count == 0) continue;

                using var paint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = stroke.Thickness,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeJoin = SKStrokeJoin.Round,
                    Color = stroke.IsEraser ? (background ?? SKColors.Transparent) : stroke.Color,
                    BlendMode = stroke.IsEraser && background is null ? SKBlendMode.Clear : SKBlendMode.SrcOver
                };

                if (stroke.Points.Count == 1)
                {
                    canvas.DrawPoint(stroke.Points[0], paint);
                    continue;
                }

                using var path = new SKPath();
                path.MoveTo(stroke.Points[0]);
                for (int i = 1; i < stroke.Points.Count; i++)
                    path.LineTo(stroke.Points[i]);

                canvas.DrawPath(path, paint);
            }
        }
    }
}
