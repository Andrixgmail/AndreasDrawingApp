# Andrea's Drawing App

A WPF (.NET 8) desktop app that turns your webcam + hand gestures into a drawing tool,
and exports the artwork as PNG or PDF.

---

## 1. Prerequisites

- **Windows 10/11 x64** (OpenCvSharp4.runtime.win and WPF are Windows-only)
- **.NET 8 SDK**: https://dotnet.microsoft.com/download
- **Visual Studio 2022** (17.8+) with the ".NET Desktop Development" workload
  or the `dotnet` CLI, both work
- A webcam

## 2. Getting the code building

```bash
cd AndreasDrawingApp
dotnet restore
dotnet build
dotnet run
```

Or open `AndreasDrawingApp.csproj` in Visual Studio and press F5.

All required NuGet packages are already declared in the `.csproj`:

| Package | Purpose |
|---|---|
| OpenCvSharp4 / OpenCvSharp4.runtime.win | Webcam capture (`VideoCapture`) |
| OpenCvSharp4.WpfExtensions | `Mat` → WPF `BitmapSource` conversion |
| Microsoft.ML.OnnxRuntime | Runs the hand-landmark ONNX model |
| SkiaSharp / SkiaSharp.Views.WPF | Drawing canvas rendering + PNG export |
| QuestPDF | Vector PDF export |

---

## 3. ONNX Model: Download & Setup

The app needs a **hand-landmark ONNX model** at:

```
AndreasDrawingApp/Assets/onnx_models/hand_landmark.onnx
```

(The `.csproj` copies everything in that folder to the output directory automatically.)

### Option A: Pre-converted community exports (fastest)

Search for **"mediapipe hand landmark onnx"** on Hugging Face or GitHub. Good starting points:

- https://github.com/PINTO0309/PINTO_model_zoo, search the repo listing for
  "hand_landmark", this project publishes many MediaPipe models pre-converted to
  ONNX/TFLite/OpenVINO with conversion scripts included.
- https://huggingface.co/models?search=hand%20landmark%20onnx, several
  community re-uploads of the MediaPipe hand landmark network in `.onnx` format.

Download the **hand landmark** model (NOT the palm-detector-only model, you want the
one that outputs 21 3D keypoints), rename it to `hand_landmark.onnx`, and drop it in
`Assets/onnx_models/`.

### Option B: Convert it yourself from the official MediaPipe model

1. Get the official task file from Google:
   `https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task`
   (a `.task` bundle contains a TFLite model inside).
2. Extract the `.tflite` hand-landmark sub-model from the bundle (it's a zip).
3. Convert TFLite → ONNX with `tf2onnx` or `onnx-tf`:
   ```bash
   pip install tf2onnx
   python -m tf2onnx.convert --opset 13 --tflite hand_landmark.tflite --output hand_landmark.onnx
   ```
4. Copy the resulting `hand_landmark.onnx` into `Assets/onnx_models/`.

### Verifying / adjusting tensor names

Different conversions can produce different input/output tensor names. Open your
`.onnx` file in **https://netron.app** (drag-and-drop, runs entirely client-side) and
check:

- **Input**: should be shape `[1, 224, 224, 3]` (float32)
- **Outputs**: one output of length **63** (21 landmarks × x,y,z) and usually a second,
  length-1 "hand presence" score.

If the names differ from the defaults in `Services/HandTrackingService.cs`
(`input_1`, `output_landmarks`, `output_handflag`), either rename them or update the
three constants at the top of that file. The service also has a same-shape fallback
heuristic so many models will "just work" without edits.

### Improving accuracy (optional, recommended for production use)

This sample runs the landmark model on a single centered square crop of the frame
every frame, a simplified stand-in for MediaPipe's real two-stage pipeline
(palm detector → cropped/rotated hand ROI → landmark model). This keeps the sample
small, but means detection can be less robust when your hand isn't near the center
of frame. To match MediaPipe's full quality:

1. Add a second ONNX model: a **palm detector** (also in the PINTO0309 model zoo).
2. Run it first on the full frame to get a hand bounding box + rotation.
3. Crop/rotate/resize that box to 224×224 and feed *that* into
   `HandTrackingService.Detect` instead of the centered-square crop.

---

## 4. Project layout

```
AndreasDrawingApp/
  App.xaml / App.xaml.cs        App startup, QuestPDF license config
  MainWindow.xaml / .cs          UI + orchestration of the whole pipeline
  Models/
    HandLandmark.cs              21-point landmark struct + named indices
    GestureType.cs                Drawing / Hover / OpenPalm / None
    DrawingStroke.cs               A single pen-down-to-pen-up path
  Services/
    CameraService.cs              Async webcam capture loop (OpenCvSharp)
    HandTrackingService.cs        ONNX Runtime inference wrapper
    GestureRecognizer.cs           Landmarks -> GestureType + clear-hold timer
    EmaSmoother.cs                 Jitter smoothing for the cursor
    DrawingCanvasManager.cs        Stroke storage + SkiaSharp rendering
    ExportService.cs               PNG (SkiaSharp) / PDF (QuestPDF) export
  Assets/onnx_models/             Put hand_landmark.onnx here
```

## 5. How the gesture logic works

| Gesture | Rule | Effect |
|---|---|---|
| **Drawing** | Index tip Y < Index PIP Y, Middle finger *not* extended | Draws a line following the index fingertip |
| **Hover** | Index *and* Middle both extended | Moves the cursor, stroke paused (no ink) |
| **Clear** | All four fingers extended (open palm), held ≥ 1.5s | Clears the canvas |
| **None** | Anything else / no hand | Ends the current stroke |

Fingertip position is smoothed with an **Exponential Moving Average**
(`Services/EmaSmoother.cs`, default α = 0.4) before being used as a draw point, to
cut down on webcam/model jitter without adding much lag.

## 6. Known limitations of this sample

- Single-hand, single-stage detection (see "Improving accuracy" above).
- Camera/canvas alignment assumes a roughly matching aspect ratio between the webcam
  feed and the window; extreme aspect-ratio mismatches will visually offset the
  overlay slightly (the underlying coordinate math is resolution-independent, so this
  is a display nicety, not a logic bug).
- No calibration step for different hand sizes/distances from camera, the
  landmark model's own scale-invariance handles most of this reasonably well.
