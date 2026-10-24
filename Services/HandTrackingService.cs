using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using AndreasDrawingApp.Models;

namespace AndreasDrawingApp.Services
{
    /// <summary>
    /// Runs a MediaPipe-style hand-landmark ONNX model via ONNX Runtime and returns
    /// 21 3D landmarks in the coordinate space of the *input* frame.
    ///
    /// IMPORTANT: MediaPipe's real pipeline is two-stage (palm detector -> crop -> landmark
    /// model). To keep this sample self-contained we run the landmark model on a single,
    /// simple square ROI (by default the full frame, letterboxed to square). This is enough
    /// to demo gesture drawing, but detection quality/range improves a lot if you add a palm
    /// detector pass first and feed it a tight crop each frame (see README "Improving accuracy").
    ///
    /// Tensor names below match the common community ONNX export of
    /// "hand_landmark_full.onnx" / "hand_landmark_sparse.onnx". If your converted model uses
    /// different names, open it in https://netron.app and update the constants below.
    /// </summary>
    public class HandTrackingService : IDisposable
    {
        private const int InputSize = 224; // model expects 224x224, channel-first (NCHW)
        private const string InputTensorName = "input";
        private const string LandmarksOutputName = "xyz_x21";   // shape [-1, 63]  (21 * xyz)
        private const string PresenceOutputName = "hand_score"; // shape [-1, 1]   hand-present score
        // Note: this model also has a "lefthand_0_or_righthand_1" output ([-1,1]) with the same shape as
        // hand_score, so we match PresenceOutputName by exact name first before any shape-based fallback.

        private readonly InferenceSession _session;
        private readonly string[] _outputNames;

        public bool ModelLoaded { get; }

        /// <summary>Human-readable summary of the actual input/output names & shapes the loaded model has.</summary>
        public string DiagnosticModelInfo { get; }

        /// <summary>The presence/confidence score from the most recent Detect() call.</summary>
        public float LastPresenceScore { get; private set; } = -1f;

        /// <summary>Set when Detect() throws internally (Detect() itself still returns null, not throws).</summary>
        public string? LastError { get; private set; }

        public HandTrackingService(string onnxModelPath)
        {
            if (!File.Exists(onnxModelPath))
                throw new FileNotFoundException(
                    $"ONNX hand landmark model not found at '{onnxModelPath}'. " +
                    "See README.md for download instructions.", onnxModelPath);

            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                // Leave one core free for the UI/capture threads so inference doesn't starve them.
                IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount - 1)
            };
            // Uncomment to use DirectML / GPU acceleration on supported Windows machines
            // (much faster than CPU if the machine has a compatible GPU):
            // options.AppendExecutionProvider_DML(0);

            _session = new InferenceSession(onnxModelPath, options);
            _outputNames = _session.OutputMetadata.Keys.ToArray();
            ModelLoaded = true;

            var inputsDesc = string.Join(", ", _session.InputMetadata.Select(kv =>
                $"{kv.Key}[{string.Join("x", kv.Value.Dimensions)}]"));
            var outputsDesc = string.Join(", ", _session.OutputMetadata.Select(kv =>
                $"{kv.Key}[{string.Join("x", kv.Value.Dimensions)}]"));
            DiagnosticModelInfo = $"IN: {inputsDesc} | OUT: {outputsDesc} " +
                                  $"| resolved input='{ResolveInputName()}' landmarks='{ResolveOutputName(LandmarksOutputName, false)}' " +
                                  $"presence='{ResolveOutputName(PresenceOutputName, true)}'";
        }

        /// <summary>
        /// Runs inference on the given BGR frame. Returns null if no hand is confidently detected.
        /// </summary>
        public HandLandmark[]? Detect(Mat bgrFrame, float presenceThreshold = 0.5f)
        {
            try
            {
                return DetectCore(bgrFrame, presenceThreshold);
            }
            catch (Exception ex)
            {
                LastError = ex.ToString();
                return null;
            }
        }

        private HandLandmark[]? DetectCore(Mat bgrFrame, float presenceThreshold)
        {
            LastError = null;
            int side = Math.Min(bgrFrame.Width, bgrFrame.Height);
            var roi = new Rect(
                (bgrFrame.Width - side) / 2,
                (bgrFrame.Height - side) / 2,
                side, side);

            using var square = new Mat(bgrFrame, roi);
            using var resized = new Mat();
            Cv2.Resize(square, resized, new Size(InputSize, InputSize));
            using var rgb = new Mat();
            Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);

            var input = new DenseTensor<float>(new[] { 1, 3, InputSize, InputSize });
            for (int y = 0; y < InputSize; y++)
            {
                for (int x = 0; x < InputSize; x++)
                {
                    var px = rgb.At<Vec3b>(y, x);
                    input[0, 0, y, x] = px.Item0 / 255f; // R
                    input[0, 1, y, x] = px.Item1 / 255f; // G
                    input[0, 2, y, x] = px.Item2 / 255f; // B
                }
            }

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(ResolveInputName(), input)
            };

            using var results = _session.Run(inputs);

            float presence = 1f;
            var presenceResult = results.FirstOrDefault(r => r.Name == ResolveOutputName(PresenceOutputName, isPresence: true));
            if (presenceResult != null)
            {
                presence = presenceResult.AsEnumerable<float>().FirstOrDefault();

                // Some converted models output a raw logit instead of a 0..1 sigmoid probability
                // (e.g. values like -4.2 or 6.8 instead of 0.02 or 0.99). Auto-detect and normalize
                // so the 0.5 threshold still means something.
                if (presence < -0.001f || presence > 1.001f)
                    presence = 1f / (1f + (float)Math.Exp(-presence));
            }
            LastPresenceScore = presence;

            if (presence < presenceThreshold)
                return null;

            var landmarksResult = results.First(r => r.Name == ResolveOutputName(LandmarksOutputName, isPresence: false));
            var flat = landmarksResult.AsEnumerable<float>().ToArray();
            if (flat.Length < HandLandmarkIndex.Count * 3)
                return null;

            var landmarks = new HandLandmark[HandLandmarkIndex.Count];
            float scaleX = (float)roi.Width / InputSize;
            float scaleY = (float)roi.Height / InputSize;

            for (int i = 0; i < HandLandmarkIndex.Count; i++)
            {
                float lx = flat[i * 3 + 0];
                float ly = flat[i * 3 + 1];
                float lz = flat[i * 3 + 2];

                // Map model-space (0..224) back to full-frame pixel coordinates.
                float px = roi.X + lx * scaleX;
                float py = roi.Y + ly * scaleY;

                landmarks[i] = new HandLandmark(px, py, lz);
            }

            return landmarks;
        }

        // Falls back gracefully if the model's actual I/O names differ from our defaults,
        // by matching on the first available input/output when the expected name is absent.
        private string ResolveInputName()
        {
            return _session.InputMetadata.ContainsKey(InputTensorName)
                ? InputTensorName
                : _session.InputMetadata.Keys.First();
        }

        private string ResolveOutputName(string preferred, bool isPresence)
        {
            if (_outputNames.Contains(preferred)) return preferred;

            // Heuristic fallback: presence/score outputs are length-1, landmark outputs are longer.
            foreach (var name in _outputNames)
            {
                var meta = _session.OutputMetadata[name];
                var dims = meta.Dimensions;
                int lastDim = dims.Length > 0 ? dims[^1] : 0;
                if (isPresence && (lastDim == 1)) return name;
                if (!isPresence && lastDim >= 63) return name;
            }

            return _outputNames.First();
        }

        public void Dispose() => _session.Dispose();
    }
}
