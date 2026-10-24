using System;
using SkiaSharp;
using AndreasDrawingApp.Models;

namespace AndreasDrawingApp.Services
{
    /// <summary>
    /// Works out where everything sits for a given canvas size. The renderer and the input
    /// code both use this, so "which peg is my hand over" always matches what is on screen.
    /// </summary>
    public sealed class HanoiLayout
    {
        public float Scale;
        public float PanelLeft, PanelRight, PanelTop, PanelBottom;
        public float BoardLeft, BoardRight, BoardTop, BoardBottom;
        public float BaseY, BaseH, ZoneW, PegH, PegW, DiscH, MinDiscW, MaxDiscW;

        public static HanoiLayout Create(int width, int height)
        {
            var l = new HanoiLayout();
            l.Scale = Math.Clamp(height / 800f, 0.6f, 1.8f);

            // Leave the right-hand side free for the floating control panel.
            l.PanelLeft = width * 0.03f;
            l.PanelRight = width * 0.81f;
            l.PanelTop = height * 0.04f;
            l.PanelBottom = height * 0.88f;

            l.BoardLeft = l.PanelLeft + 20 * l.Scale;
            l.BoardRight = l.PanelRight - 20 * l.Scale;
            l.BoardTop = l.PanelTop + 92 * l.Scale;
            l.BoardBottom = l.PanelBottom - 16 * l.Scale;

            float boardH = l.BoardBottom - l.BoardTop;
            l.BaseH = boardH * 0.07f;
            l.BaseY = l.BoardBottom - l.BaseH;

            float usable = l.BaseY - l.BoardTop;
            l.DiscH = Math.Min(usable * 0.11f, 70 * l.Scale);
            l.PegH = Math.Min(usable * 0.95f, l.DiscH * (HanoiGame.MaxDiscCount + 1.6f));
            l.PegW = 18 * l.Scale;

            l.ZoneW = (l.BoardRight - l.BoardLeft) / HanoiGame.PegCount;
            l.MinDiscW = l.ZoneW * 0.30f;
            l.MaxDiscW = l.ZoneW * 0.90f;
            return l;
        }

        public float PegX(int index) => BoardLeft + ZoneW * (index + 0.5f);

        public int PegAt(float x) =>
            Math.Clamp((int)MathF.Floor((x - BoardLeft) / ZoneW), 0, HanoiGame.PegCount - 1);

        public float DiscWidth(int size, int count)
        {
            if (count <= 1) return MaxDiscW;
            return MinDiscW + (MaxDiscW - MinDiscW) * (size - 1) / (count - 1);
        }
    }

    /// <summary>Draws the Tower of Hanoi board on a SkiaSharp canvas.</summary>
    public static class HanoiRenderer
    {
        private static readonly SKTypeface BoldFace =
            SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;

        // Rainbow colors like the wooden toy: smallest disc is pink, largest is blue.
        private static readonly SKColor[] Palette =
        {
            new SKColor(255, 82, 170),
            new SKColor(255, 140, 30),
            new SKColor(255, 214, 0),
            new SKColor(70, 200, 90),
            new SKColor(40, 150, 255),
        };

        public static void Render(SKCanvas canvas, int width, int height, HanoiGame game,
                                  SKPoint? cursor, bool pinching, double holdProgress)
        {
            canvas.Clear(SKColors.Transparent);
            var l = HanoiLayout.Create(width, height);

            DrawPanel(canvas, l);
            DrawHeader(canvas, l, game);

            if (cursor is { } c && !game.IsSolved)
                DrawZoneHighlight(canvas, l, l.PegAt(c.X), game.HeldDisc != null);

            DrawBaseAndPegs(canvas, l);
            DrawStackedDiscs(canvas, l, game);

            if (game.HeldDisc is int held && cursor is { } cur)
                DrawDisc(canvas, l, held, game.DiscCount, cur.X, cur.Y, lifted: true);

            if (cursor is { } cc)
                DrawCursor(canvas, l, cc, pinching);

            if (game.IsSolved)
                DrawSolvedBanner(canvas, l, game, holdProgress);
        }

        private static void DrawPanel(SKCanvas canvas, HanoiLayout l)
        {
            var rect = new SKRect(l.PanelLeft, l.PanelTop, l.PanelRight, l.PanelBottom);
            using var fill = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 150) };
            using var border = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2,
                Color = new SKColor(255, 255, 255, 70)
            };
            canvas.DrawRoundRect(rect, 18 * l.Scale, 18 * l.Scale, fill);
            canvas.DrawRoundRect(rect, 18 * l.Scale, 18 * l.Scale, border);
        }

        private static void DrawHeader(SKCanvas canvas, HanoiLayout l, HanoiGame game)
        {
            string title = $"Tower of Hanoi    Level {game.LevelIndex + 1} of {HanoiGame.LevelDiscCounts.Length}" +
                           $"    Moves: {game.Moves}    Best possible: {game.OptimalMoves}";
            DrawText(canvas, title, l.BoardLeft, l.PanelTop + 36 * l.Scale, 26 * l.Scale, SKColors.White);
            DrawText(canvas, game.Message, l.BoardLeft, l.PanelTop + 70 * l.Scale, 19 * l.Scale,
                     new SKColor(215, 215, 215));
        }

        private static void DrawZoneHighlight(SKCanvas canvas, HanoiLayout l, int peg, bool holding)
        {
            float left = l.BoardLeft + l.ZoneW * peg;
            var rect = new SKRect(left + 4, l.BoardTop, left + l.ZoneW - 4, l.BoardBottom);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = holding ? new SKColor(255, 214, 0, 60) : new SKColor(255, 255, 255, 35)
            };
            canvas.DrawRoundRect(rect, 14 * l.Scale, 14 * l.Scale, paint);
        }

        private static void DrawBaseAndPegs(SKCanvas canvas, HanoiLayout l)
        {
            var wood = new SKColor(222, 184, 135);
            var woodDark = new SKColor(160, 115, 65);

            using var fill = new SKPaint { IsAntialias = true, Color = wood };
            using var outline = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2,
                Color = woodDark
            };

            var plate = new SKRect(l.BoardLeft - 10 * l.Scale, l.BaseY, l.BoardRight + 10 * l.Scale, l.BaseY + l.BaseH);
            canvas.DrawRoundRect(plate, 10 * l.Scale, 10 * l.Scale, fill);
            canvas.DrawRoundRect(plate, 10 * l.Scale, 10 * l.Scale, outline);

            for (int i = 0; i < HanoiGame.PegCount; i++)
            {
                float x = l.PegX(i);
                var peg = new SKRect(x - l.PegW / 2, l.BaseY - l.PegH, x + l.PegW / 2, l.BaseY + 2);
                canvas.DrawRoundRect(peg, l.PegW / 2, l.PegW / 2, fill);
                canvas.DrawRoundRect(peg, l.PegW / 2, l.PegW / 2, outline);
            }

            float labelSize = Math.Min(l.BaseH * 0.55f, 20 * l.Scale);
            float labelY = l.BaseY + l.BaseH / 2 + labelSize * 0.35f;
            DrawText(canvas, "START", l.PegX(0), labelY, labelSize, woodDark, center: true);
            DrawText(canvas, "GOAL", l.PegX(HanoiGame.PegCount - 1), labelY, labelSize, woodDark, center: true);
        }

        private static void DrawStackedDiscs(SKCanvas canvas, HanoiLayout l, HanoiGame game)
        {
            for (int peg = 0; peg < HanoiGame.PegCount; peg++)
            {
                var discs = game.GetPeg(peg);
                for (int k = 0; k < discs.Count; k++)
                {
                    float centerY = l.BaseY - (k + 0.5f) * l.DiscH;
                    DrawDisc(canvas, l, discs[k], game.DiscCount, l.PegX(peg), centerY, lifted: false);
                }
            }
        }

        private static void DrawDisc(SKCanvas canvas, HanoiLayout l, int size, int count,
                                     float centerX, float centerY, bool lifted)
        {
            float w = l.DiscWidth(size, count);
            float h = l.DiscH - 4 * l.Scale;
            var rect = new SKRect(centerX - w / 2, centerY - h / 2, centerX + w / 2, centerY + h / 2);
            var color = ColorFor(size, count);
            float radius = h / 2;

            if (lifted)
            {
                var shadowRect = rect;
                shadowRect.Offset(0, 8 * l.Scale);
                using var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 80) };
                canvas.DrawRoundRect(shadowRect, radius, radius, shadow);
            }

            using var shader = SKShader.CreateLinearGradient(
                new SKPoint(0, rect.Top), new SKPoint(0, rect.Bottom),
                new[] { Lighten(color, 0.35f), color },
                null, SKShaderTileMode.Clamp);
            using var fill = new SKPaint { IsAntialias = true, Shader = shader };
            using var outline = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2 * l.Scale,
                Color = Darken(color, 0.35f)
            };
            canvas.DrawRoundRect(rect, radius, radius, fill);
            canvas.DrawRoundRect(rect, radius, radius, outline);

            var shine = new SKRect(rect.Left + h * 0.5f, rect.Top + h * 0.12f, rect.Right - h * 0.5f, rect.Top + h * 0.36f);
            if (shine.Width > 4)
            {
                using var shinePaint = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 90) };
                canvas.DrawRoundRect(shine, h * 0.12f, h * 0.12f, shinePaint);
            }
        }

        private static void DrawCursor(SKCanvas canvas, HanoiLayout l, SKPoint p, bool pinching)
        {
            float r = 20 * l.Scale;
            using var dark = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 6 * l.Scale,
                Color = new SKColor(0, 0, 0, 140)
            };
            using var ring = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3 * l.Scale,
                Color = pinching ? new SKColor(255, 214, 0) : SKColors.White
            };
            canvas.DrawCircle(p, r, dark);
            canvas.DrawCircle(p, r, ring);

            if (pinching)
            {
                using var dot = new SKPaint { IsAntialias = true, Color = new SKColor(255, 214, 0, 220) };
                canvas.DrawCircle(p, r * 0.5f, dot);
            }
        }

        private static void DrawSolvedBanner(SKCanvas canvas, HanoiLayout l, HanoiGame game, double holdProgress)
        {
            float boardW = l.BoardRight - l.BoardLeft;
            float w = boardW * 0.85f;
            float h = 190 * l.Scale;
            float cx = (l.BoardLeft + l.BoardRight) / 2;
            float cy = (l.BoardTop + l.BoardBottom) / 2;
            var rect = new SKRect(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2);

            using var fill = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 215) };
            using var border = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3,
                Color = new SKColor(255, 214, 0)
            };
            canvas.DrawRoundRect(rect, 20 * l.Scale, 20 * l.Scale, fill);
            canvas.DrawRoundRect(rect, 20 * l.Scale, 20 * l.Scale, border);

            string headline = game.HasNextLevel ? "Level complete!" : "You finished every level!";
            string hint = game.HasNextLevel
                ? "Hold an open palm for 1.5 seconds to start the next level"
                : "Hold an open palm for 1.5 seconds to play again from level 1";

            DrawText(canvas, headline, cx, rect.Top + 56 * l.Scale, 42 * l.Scale, new SKColor(255, 214, 0), center: true);
            DrawText(canvas, game.Message, cx, rect.Top + 96 * l.Scale, 22 * l.Scale, SKColors.White, center: true);
            DrawText(canvas, hint, cx, rect.Top + 130 * l.Scale, 19 * l.Scale, new SKColor(215, 215, 215), center: true);

            float barW = 300 * l.Scale;
            float barH = 12 * l.Scale;
            var track = new SKRect(cx - barW / 2, rect.Bottom - 30 * l.Scale, cx + barW / 2, rect.Bottom - 30 * l.Scale + barH);
            using var trackPaint = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 60) };
            canvas.DrawRoundRect(track, barH / 2, barH / 2, trackPaint);

            float filled = barW * (float)Math.Clamp(holdProgress, 0, 1);
            if (filled > barH)
            {
                var bar = new SKRect(track.Left, track.Top, track.Left + filled, track.Bottom);
                using var barPaint = new SKPaint { IsAntialias = true, Color = new SKColor(70, 200, 90) };
                canvas.DrawRoundRect(bar, barH / 2, barH / 2, barPaint);
            }
        }

        private static void DrawText(SKCanvas canvas, string text, float x, float y, float size,
                                     SKColor color, bool center = false)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = color,
                Typeface = BoldFace,
                TextSize = size
            };
            if (center) x -= paint.MeasureText(text) / 2f;
            canvas.DrawText(text, x, y, paint);
        }

        private static SKColor ColorFor(int size, int count)
        {
            if (count <= 1) return Palette[0];
            int index = (int)MathF.Round((size - 1) * (Palette.Length - 1) / (float)(count - 1));
            return Palette[Math.Clamp(index, 0, Palette.Length - 1)];
        }

        private static SKColor Lighten(SKColor c, float amount) => new SKColor(
            (byte)(c.Red + (255 - c.Red) * amount),
            (byte)(c.Green + (255 - c.Green) * amount),
            (byte)(c.Blue + (255 - c.Blue) * amount));

        private static SKColor Darken(SKColor c, float amount) => new SKColor(
            (byte)(c.Red * (1 - amount)),
            (byte)(c.Green * (1 - amount)),
            (byte)(c.Blue * (1 - amount)));
    }
}
