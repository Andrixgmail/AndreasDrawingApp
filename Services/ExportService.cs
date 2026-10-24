using System.Globalization;
using System.IO;
using System.Text;
using SkiaSharp;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using AndreasDrawingApp.Models;

namespace AndreasDrawingApp.Services
{
    /// <summary>
    /// Exports the current drawing to PNG (raster, via SkiaSharp) or PDF (vector, via QuestPDF).
    /// </summary>
    public static class ExportService
    {
        /// <summary>
        /// Renders the drawing (white background) to a PNG file.
        /// </summary>
        public static void SavePng(DrawingCanvasManager canvasManager, int width, int height, string filePath)
        {
            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvasManager.Render(canvas, background: SKColors.White);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(filePath);
            data.SaveTo(stream);
        }

        /// <summary>
        /// Renders the drawing as true vector paths into a single-page PDF.
        /// QuestPDF 2024.3.0+ removed the raw Canvas() escape hatch, and SkiaSharp's SKSVGCanvas
        /// isn't reliably available across SkiaSharp package versions/platforms. Since we already
        /// have full control over the stroke data (points, color, thickness), we build the SVG
        /// markup ourselves and hand that string to QuestPDF's .Svg() content API. Output is still
        /// true vector artwork, not a raster screenshot.
        /// </summary>
        public static void SavePdf(DrawingCanvasManager canvasManager, int width, int height, string filePath)
        {
            string svgContent = BuildSvgString(canvasManager, width, height);

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(width, height, Unit.Point);
                    page.Margin(0);
                    page.Background(Colors.White);

                    page.Content().Svg(svgContent);
                });
            })
            .GeneratePdf(filePath);
        }

        private static string BuildSvgString(DrawingCanvasManager canvasManager, int width, int height)
        {
            var ic = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();

            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
            // White page background baked into the SVG itself (page.Background() only covers the
            // PDF page margins, not the SVG's own canvas area).
            sb.Append($"<rect x=\"0\" y=\"0\" width=\"{width}\" height=\"{height}\" fill=\"#FFFFFF\"/>");

            foreach (var stroke in canvasManager.Strokes)
            {
                if (stroke.Points.Count == 0) continue;

                // Eraser strokes paint white to match the page background, same as the PNG export.
                var color = stroke.IsEraser ? SKColors.White : stroke.Color;
                string hex = $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

                if (stroke.Points.Count == 1)
                {
                    var p = stroke.Points[0];
                    float r = stroke.Thickness / 2f;
                    sb.Append($"<circle cx=\"{p.X.ToString(ic)}\" cy=\"{p.Y.ToString(ic)}\" r=\"{r.ToString(ic)}\" fill=\"{hex}\"/>");
                    continue;
                }

                sb.Append("<path d=\"M ");
                sb.Append(stroke.Points[0].X.ToString(ic)).Append(' ').Append(stroke.Points[0].Y.ToString(ic));
                for (int i = 1; i < stroke.Points.Count; i++)
                {
                    sb.Append(" L ");
                    sb.Append(stroke.Points[i].X.ToString(ic)).Append(' ').Append(stroke.Points[i].Y.ToString(ic));
                }
                sb.Append("\" fill=\"none\" stroke=\"").Append(hex)
                  .Append("\" stroke-width=\"").Append(stroke.Thickness.ToString(ic))
                  .Append("\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
            }

            sb.Append("</svg>");
            return sb.ToString();
        }
    }
}
