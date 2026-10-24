# Andrea's Drawing App: User Manual

## What it is

Andrea's Drawing App lets you draw in mid-air. Point your index finger at your webcam
and move it around, the app tracks your fingertip and turns its motion into strokes
on a virtual canvas, live over your video feed. When you're done, save your artwork
as a PNG image or a PDF document.

## Getting started

1. Launch **AndreasDrawingApp.exe** (or press F5 in Visual Studio).
2. Click **▶ Start Camera** in the control panel (top-right).
3. Allow camera access if Windows prompts you.
4. Hold your hand up in front of the camera, about arm's length away, so your whole
   hand is visible in frame.
5. Raise your **index finger** and you'll see the status readout (bottom-left) switch
   to **"Drawing ✏️"** and a line will start following your fingertip.

## Hand gestures

| Gesture | How to do it | What happens |
|---|---|---|
| ✏️ **Draw** | Point with your index finger only (middle finger curled down) | Ink follows your fingertip |
| ✋ **Hover / move** | Extend both your index *and* middle finger (like a "peace sign" with two fingers) | Cursor moves, but nothing is drawn; use this to reposition without leaving a trail |
| 🖐 **Clear canvas** | Open your whole hand flat, palm toward the camera, and **hold it for 1.5 seconds** | Wipes the canvas clean. A progress bar (bottom-left, "Clear hold") fills up as you hold the pose; let go early to cancel |
| ⏹ **Stop drawing** | Lower your hand, make a fist, or move it out of frame | The current stroke ends; raising your index finger again starts a brand-new stroke |

**Tip:** if strokes look shaky, move a little slower and make sure you're well lit.
The tracking works best with even lighting and a plain background behind your hand.

## The control panel

Located top-right, floating over the video:

- **Stroke Color**: six color swatches (red, orange, gold, lime, sky blue, purple).
  Click one to switch the ink color for new strokes.
- **Thickness**: slider from 2 to 30 pixels. Adjust before drawing a stroke; existing
  strokes keep whatever thickness they were drawn with.
- **Eraser Mode**: check this box to turn your fingertip into an eraser instead of a
  pen. Uncheck to go back to drawing.
- **🧹 Clear Canvas**: instantly wipes everything (same effect as the open-palm
  gesture, but immediate, no button-press-and-wait needed).
- **💾 Save as PNG**: opens a save dialog; your drawing is flattened onto a white
  background and saved as a `.png` image.
- **📄 Save as PDF**: opens a save dialog; your drawing is saved as a single-page
  vector PDF (so it stays crisp at any zoom level or print size).
- **▶ Start Camera / ⏸ Stop Camera**: toggles the webcam feed and tracking on or off.

## Status readouts

- **Gesture** (bottom-left): shows what the app currently thinks your hand is doing:
  Drawing, Hover, Open Palm, or "No hand detected."
- **Clear hold** progress bar: fills up while you hold an open-palm pose; reaching
  full triggers the canvas clear.
- **Footer bar** (very bottom): general status messages: camera state, save
  confirmations, and warnings (e.g. if the hand-tracking model file is missing).

## Saving your work

1. Click **Save as PNG** or **Save as PDF**.
2. Choose a folder and file name in the dialog that appears.
3. Click **Save**. The footer bar will confirm the file path once it's written.

Your saved file contains only the *drawing* (on a white background), the live camera
feed itself is not included in the export, so your artwork is clean and shareable.

## Troubleshooting

| Problem | Likely cause / fix |
|---|---|
| "Hand-tracking model not loaded" warning in footer | The ONNX model file is missing. See README.md, section "ONNX Model: Download & Setup," and place `hand_landmark.onnx` in `Assets/onnx_models/`. |
| Camera preview is black / "Could not open camera" | Another app may be using the webcam, or Windows camera privacy settings are blocking access. Close other video apps and check Settings → Privacy → Camera. |
| Drawing lags behind my finger | Lower the resolution requested in `MainWindow.xaml.cs` (`_frameWidth`/`_frameHeight`), or enable GPU acceleration (see the DirectML note in `HandTrackingService.cs`). |
| Strokes are jittery | Improve lighting, use a plainer background, or increase the smoothing by lowering `alpha` in `EmaSmoother` (e.g. 0.4 → 0.25). |
| Gesture doesn't register | Make sure your whole hand, not just fingertips, is inside the frame; the model needs the full hand to estimate joint positions. |

---

# Suggested "cool" features to add next

Ideas for extending the app, roughly ordered from easiest to most involved:

1. **Undo / Redo**: keep strokes in a stack; Ctrl+Z pops the last stroke. Trivial
   since `DrawingCanvasManager` already stores strokes as a list.
2. **Pinch-to-select stroke width**: measure distance between thumb tip (landmark 4)
   and index tip (landmark 8) and map it to line thickness in real time, so you can
   "pinch" thinner or wider without touching the slider.
3. **Two-hand support**: track both hands independently (run the model on two ROIs,
   one per detected hand) so one hand draws while the other picks colors from a
   gesture-based palette.
4. **Shape recognition**: detect simple closed loops (circle, rectangle, arrow) drawn
   in the air and "snap" them to a perfect vector shape, like a whiteboard app's
   auto-shape mode.
5. **Layers**: let each stroke belong to a layer, with a UI panel to hide/reorder/
   merge layers before export.
6. **Color-follows-emotion / voice command mode**: add speech recognition
   (`System.Speech` or Azure Speech SDK) so you can say "red," "thicker," "clear" as
   an alternative to gestures.
7. **Session recording**: record the stroke *sequence* (not just the final canvas)
   and add a "replay" button that redraws your artwork stroke-by-stroke like a
   timelapse; export that as an animated GIF or MP4.
8. **Symmetry / mirror mode**: mirror every stroke across a vertical or radial axis
   for kaleidoscope-style drawing (fun for kids' mode).
9. **Multi-page PDF sketchbook**: keep a history of cleared canvases and export the
   whole session as a multi-page PDF "sketchbook" instead of a single page.
10. **Cloud save / share link**: hook up a simple upload to a storage bucket and
    generate a shareable link after PNG export.
11. **Pressure-simulated brush**: vary stroke width along its length based on drawing
    speed (faster finger motion = thinner line), for a more natural pen feel.
12. **Custom brush textures**: swap the flat `SKPaint` stroke for a textured
    `SKShader` (chalk, marker, spray-paint) for different artistic looks.
