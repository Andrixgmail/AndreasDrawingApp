using System;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace AndreasDrawingApp.Services
{
    /// <summary>
    /// Owns the webcam VideoCapture and reads frames continuously on a background task.
    /// Raises FrameReady on every successfully captured, mirrored frame.
    /// The Mat passed to the event is owned by the caller and must be Dispose()'d.
    /// </summary>
    public class CameraService : IDisposable
    {
        private VideoCapture? _capture;
        private CancellationTokenSource? _cts;
        private Task? _captureTask;

        public bool IsRunning => _captureTask is { IsCompleted: false };

        public int FrameWidth { get; private set; }
        public int FrameHeight { get; private set; }

        /// <summary>Fired from a background thread for every captured frame (already mirrored).</summary>
        public event Action<Mat>? FrameReady;

        /// <summary>Fired (on the background thread) if the camera fails to open or read.</summary>
        public event Action<string>? CameraError;

        public bool Start(int cameraIndex = 0, int requestedWidth = 1280, int requestedHeight = 720)
        {
            if (IsRunning) return true;

            _capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);
            if (!_capture.IsOpened())
            {
                _capture.Dispose();
                _capture = null;
                CameraError?.Invoke($"Could not open camera index {cameraIndex}.");
                return false;
            }

            _capture.Set(VideoCaptureProperties.FrameWidth, requestedWidth);
            _capture.Set(VideoCaptureProperties.FrameHeight, requestedHeight);

            FrameWidth = (int)_capture.Get(VideoCaptureProperties.FrameWidth);
            FrameHeight = (int)_capture.Get(VideoCaptureProperties.FrameHeight);

            _cts = new CancellationTokenSource();
            _captureTask = Task.Run(() => CaptureLoop(_cts.Token), _cts.Token);
            return true;
        }

        private void CaptureLoop(CancellationToken token)
        {
            using var raw = new Mat();
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_capture == null || !_capture.Read(raw) || raw.Empty())
                    {
                        Task.Delay(10, token).Wait(token);
                        continue;
                    }

                    // Mirror horizontally so hand movement feels natural (like a mirror).
                    var mirrored = new Mat();
                    Cv2.Flip(raw, mirrored, FlipMode.Y);

                    FrameReady?.Invoke(mirrored); // subscriber owns disposal
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    CameraError?.Invoke(ex.Message);
                }
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _captureTask?.Wait(500); } catch { /* ignore */ }
            _capture?.Release();
            _capture?.Dispose();
            _capture = null;
            _cts?.Dispose();
            _cts = null;
        }

        public void Dispose() => Stop();
    }
}
