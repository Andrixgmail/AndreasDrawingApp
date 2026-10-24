using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using AndreasDrawingApp.Models;
using AndreasDrawingApp.Services;
using WpfWindow = System.Windows.Window;

namespace AndreasDrawingApp
{
    public partial class MainWindow : WpfWindow
    {
        private readonly CameraService _cameraService = new();
        private readonly GestureRecognizer _gestureRecognizer = new();
        private readonly DrawingCanvasManager _canvasManager = new();
        private readonly EmaSmoother _emaSmoother = new(alpha: 0.4f);

        private HandTrackingService? _handTracker;

        private int _frameWidth = 1280;
        private int _frameHeight = 720;

        // Updated whenever the Skia surface repaints; used to map camera-pixel
        // coordinates into on-screen drawing-surface coordinates.
        private int _canvasPixelWidth = 1280;
        private int _canvasPixelHeight = 720;

        private GestureType _previousGesture = GestureType.None;
        private bool _isDrawingStrokeActive;

        // Decoupled inference pipeline
        // The camera capture loop (CameraService) calls OnFrameReady synchronously for every
        // frame. Running ONNX inference inline there would force the capture loop to wait for
        // inference each frame, capping the whole app (preview + drawing + detection) at
        // inference speed. Instead: OnFrameReady only updates the preview image (fast) and hands
        // off a clone of the latest frame to a separate inference loop. That loop always grabs
        // the newest available frame and discards anything older it hasn't gotten to yet, so a
        // slow inference pass causes dropped frames (skipped gesture updates), not growing lag.
        private readonly object _frameLock = new();
        private Mat? _latestFrameForInference;
        private CancellationTokenSource? _inferenceCts;
        private Task? _inferenceTask;

        // Tower of Hanoi game mode
        private readonly HanoiGame _game = new();
        private readonly PinchDetector _pinch = new();
        private readonly EmaSmoother _gameSmoother = new(alpha: 0.5f);
        private volatile bool _isGameMode;
        private SKPoint? _gameCursor;        // where the cursor ring is drawn (canvas pixels)
        private SKPoint _lastHeldCursor;     // cursor position during the most recent pinch frame
        private bool _wasPinching;           // pinch state from the previous update
        private bool _mouseDown;             // mouse is acting as a pinch (testing without a camera)
        private DateTime _lastMouseActivity = DateTime.MinValue;
        private DateTime? _handLostSince;
        private static readonly TimeSpan HandLostGrace = TimeSpan.FromMilliseconds(800);

        public MainWindow()
        {
            InitializeComponent();
            UpdateModeButtons();
            Loaded += MainWindow_Loaded;
            Closing += (_, _) =>
            {
                _cameraService.Stop();
                StopInferenceLoop();
            };
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            TryLoadHandTrackingModel();
        }

        private void TryLoadHandTrackingModel()
        {
            var modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "Assets", "onnx_models", "hand_landmark.onnx");
            try
            {
                _handTracker = new HandTrackingService(modelPath);
                FooterStatusText.Text = "Model loaded. " + _handTracker.DiagnosticModelInfo;
            }
            catch (Exception ex)
            {
                _handTracker = null;
                FooterStatusText.Text =
                    $"⚠ Hand-tracking model not loaded ({ex.Message}). Camera preview will still work, " +
                    "but gesture drawing is disabled until an ONNX model is placed at Assets\\onnx_models\\hand_landmark.onnx.";
            }
        }

        // Camera lifecycle

        private void StartStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_cameraService.IsRunning)
            {
                _cameraService.Stop();
                StopInferenceLoop();
                StartStopButton.Content = "▶ Start Camera";
                FooterStatusText.Text = "Camera stopped.";
                return;
            }

            _cameraService.FrameReady -= OnFrameReady;
            _cameraService.FrameReady += OnFrameReady;
            _cameraService.CameraError -= OnCameraError;
            _cameraService.CameraError += OnCameraError;

            bool started = _cameraService.Start(cameraIndex: 0, requestedWidth: _frameWidth, requestedHeight: _frameHeight);
            if (started)
            {
                _frameWidth = _cameraService.FrameWidth;
                _frameHeight = _cameraService.FrameHeight;
                StartStopButton.Content = "⏸ Stop Camera";
                FooterStatusText.Text = _isGameMode
                    ? "Camera running. Pinch your thumb and index finger over a tower to lift a disc."
                    : "Camera running. Raise your index finger to draw!";

                _inferenceCts = new CancellationTokenSource();
                _inferenceTask = Task.Run(() => InferenceLoop(_inferenceCts.Token));
            }
        }

        private void StopInferenceLoop()
        {
            _inferenceCts?.Cancel();
            try { _inferenceTask?.Wait(500); } catch { /* ignore */ }
            _inferenceCts?.Dispose();
            _inferenceCts = null;
            _inferenceTask = null;

            lock (_frameLock)
            {
                _latestFrameForInference?.Dispose();
                _latestFrameForInference = null;
            }
        }

        private void OnCameraError(string message)
        {
            Dispatcher.BeginInvoke(() => FooterStatusText.Text = $"Camera error: {message}");
        }

        // Per-frame pipeline (runs on the CameraService background thread)

        // Fast path: runs for every captured frame. Only updates the live preview image and
        // hands off a clone to the inference loop and does NOT run ONNX here, so the camera's
        // own frame rate is never gated by how long detection takes.
        private void OnFrameReady(Mat frame)
        {
            try
            {
                BitmapSource bitmap = frame.ToBitmapSource();
                bitmap.Freeze();
                Dispatcher.BeginInvoke(() => CameraImage.Source = bitmap);

                var clone = frame.Clone();
                lock (_frameLock)
                {
                    // Drop whatever the inference loop hasn't gotten to yet; we only ever
                    // want the newest frame, never a growing backlog.
                    _latestFrameForInference?.Dispose();
                    _latestFrameForInference = clone;
                }
            }
            finally
            {
                frame.Dispose(); // we own it per CameraService's contract
            }
        }

        // Slow path: runs continuously on its own background task, independent of the camera's
        // frame rate. Always processes the most recent frame available; if it's still busy with
        // one frame when a newer one arrives, the older one is simply discarded (no backlog).
        private async Task InferenceLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                Mat? toProcess = null;
                lock (_frameLock)
                {
                    if (_latestFrameForInference is not null)
                    {
                        toProcess = _latestFrameForInference;
                        _latestFrameForInference = null;
                    }
                }

                if (toProcess is null)
                {
                    try { await Task.Delay(5, token); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }

                using (toProcess)
                {
                    HandLandmark[]? landmarks = null;
                    try
                    {
                        landmarks = _handTracker?.Detect(toProcess);
                    }
                    catch
                    {
                        landmarks = null; // fail closed: no drawing this frame
                    }

                    if (_isGameMode)
                    {
                        // Open palm is only used in the game to continue after a level is solved.
                        GestureType palm = _gestureRecognizer.Classify(landmarks);
                        double palmProgress = _gestureRecognizer.ClearProgress;
                        bool palmDone = palm == GestureType.OpenPalm && _gestureRecognizer.ShouldTriggerClear();

                        bool handPresent = landmarks is not null;
                        SKPoint? cursor = null;
                        bool pinching = false;

                        if (landmarks is not null)
                        {
                            _pinch.Update(landmarks);
                            pinching = _pinch.IsPinching;
                            var c = _pinch.Cursor;
                            var rawPoint = new SKPoint(c.X / _frameWidth * _canvasPixelWidth,
                                                       c.Y / _frameHeight * _canvasPixelHeight);
                            cursor = _gameSmoother.Smooth(rawPoint);
                        }
                        else
                        {
                            _pinch.Reset();
                            _gameSmoother.Reset();
                        }

                        if (token.IsCancellationRequested) break;

                        try
                        {
                            await Dispatcher.InvokeAsync(() =>
                                ApplyGameInput(handPresent, cursor, pinching, palmProgress, palmDone));
                        }
                        catch (TaskCanceledException) { break; }
                        continue;
                    }

                    GestureType gesture = _gestureRecognizer.Classify(landmarks);
                    double clearProgress = _gestureRecognizer.ClearProgress;

                    SKPoint? canvasPoint = null;
                    if (landmarks is not null && (gesture == GestureType.Drawing || gesture == GestureType.Hover))
                    {
                        var tip = landmarks[HandLandmarkIndex.IndexTip];
                        float fracX = tip.X / _frameWidth;
                        float fracY = tip.Y / _frameHeight;

                        var raw = new SKPoint(fracX * _canvasPixelWidth, fracY * _canvasPixelHeight);
                        canvasPoint = _emaSmoother.Smooth(raw);
                    }
                    else
                    {
                        _emaSmoother.Reset();
                    }

                    bool triggerClear = gesture == GestureType.OpenPalm && _gestureRecognizer.ShouldTriggerClear();

                    string? debugText = null;
                    if (_handTracker is not null && gesture == GestureType.None)
                    {
                        debugText = _handTracker.LastError is { } err
                            ? $"Inference error: {err.Replace('\n', ' ').Replace('\r', ' ')[..Math.Min(400, err.Length)]}"
                            : $"No hand, last presence score: {_handTracker.LastPresenceScore:F3} (threshold 0.5)";
                    }

                    if (token.IsCancellationRequested) break;

                    try
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            ApplyGestureResult(gesture, canvasPoint, clearProgress, triggerClear);
                            if (debugText is not null) FooterStatusText.Text = debugText;
                        });
                    }
                    catch (TaskCanceledException) { break; }
                }
            }
        }

        private void ApplyGestureResult(GestureType gesture, SKPoint? canvasPoint, double clearProgress, bool triggerClear)
        {
            if (_isGameMode) return; // a stale draw-mode update that arrived after switching modes

            GestureStatusText.Text = gesture switch
            {
                GestureType.Drawing => "Drawing ✏️",
                GestureType.Hover => "Hover ✋",
                GestureType.OpenPalm => "Open Palm (hold to clear) 🖐",
                _ => "No hand detected"
            };
            ClearProgressBar.Value = clearProgress;

            if (triggerClear)
            {
                _canvasManager.Clear();
                _gestureRecognizer.ResetClearTimer();
                _isDrawingStrokeActive = false;
                _previousGesture = gesture;
                DrawingSurface.InvalidateVisual();
                return;
            }

            switch (gesture)
            {
                case GestureType.Drawing when canvasPoint is { } p:
                    if (!_isDrawingStrokeActive)
                    {
                        _canvasManager.BeginStroke(p);
                        _isDrawingStrokeActive = true;
                    }
                    else
                    {
                        _canvasManager.ExtendStroke(p);
                    }
                    break;

                default:
                    if (_isDrawingStrokeActive)
                    {
                        _canvasManager.EndStroke();
                        _isDrawingStrokeActive = false;
                    }
                    break;
            }

            _previousGesture = gesture;
            DrawingSurface.InvalidateVisual();
        }

        // Tower of Hanoi game input

        // Runs on the UI thread. Takes the hand state from the inference loop and turns it
        // into game actions: pinch starts = lift the top disc, pinch ends = drop it.
        private void ApplyGameInput(bool handPresent, SKPoint? cursor, bool pinching,
                                    double palmProgress, bool palmDone)
        {
            if (!_isGameMode || _mouseDown) return; // the mouse has control while the button is held

            if (_game.IsSolved)
            {
                ClearProgressBar.Value = palmProgress;
                GestureStatusText.Text = handPresent ? "Hold open palm to continue" : "No hand detected";
                _gameCursor = cursor;

                if (palmDone)
                {
                    _gestureRecognizer.ResetClearTimer();
                    _game.AdvanceAfterSolved();
                    _wasPinching = false;
                    ClearProgressBar.Value = 0;
                }

                DrawingSurface.InvalidateVisual();
                return;
            }

            ClearProgressBar.Value = 0;

            if (!handPresent)
            {
                GestureStatusText.Text = "No hand detected";
                _handLostSince ??= DateTime.UtcNow;

                // Keep carrying the disc for a moment in case tracking just dropped a frame or two.
                if (_game.HeldDisc != null && DateTime.UtcNow - _handLostSince.Value > HandLostGrace)
                {
                    _game.CancelHold();
                    _wasPinching = false;
                }

                bool mouseRecentlyUsed = DateTime.UtcNow - _lastMouseActivity < TimeSpan.FromSeconds(1);
                if (_game.HeldDisc == null && !mouseRecentlyUsed) _gameCursor = null;

                DrawingSurface.InvalidateVisual();
                return;
            }

            _handLostSince = null;
            GestureStatusText.Text = pinching ? "Pinching 🤏" : "Open hand ✋";
            HandleGameCursor(cursor!.Value, pinching);
        }

        // Shared by hand tracking and the mouse so both behave identically.
        private void HandleGameCursor(SKPoint cursor, bool pinching)
        {
            var layout = HanoiLayout.Create(_canvasPixelWidth, _canvasPixelHeight);
            _gameCursor = cursor;

            if (pinching) _lastHeldCursor = cursor;

            if (pinching && !_wasPinching)
            {
                _game.TryPickUp(layout.PegAt(cursor.X));
            }
            else if (!pinching && _wasPinching && _game.HeldDisc != null)
            {
                // Drop where the hand was while still pinching, because fingers drift
                // a little as they open.
                _game.Drop(layout.PegAt(_lastHeldCursor.X));
            }

            _wasPinching = pinching;
            DrawingSurface.InvalidateVisual();
        }

        // Mouse fallback (handy for testing the game without a camera)

        private SKPoint? MouseToCanvas(MouseEventArgs e)
        {
            var pos = e.GetPosition(CameraHost);
            double w = CameraHost.ActualWidth;
            double h = CameraHost.ActualHeight;
            if (w <= 0 || h <= 0) return null;
            return new SKPoint((float)(pos.X / w * _canvasPixelWidth), (float)(pos.Y / h * _canvasPixelHeight));
        }

        private void CameraHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isGameMode || _game.IsSolved) return;
            if (MouseToCanvas(e) is not { } p) return;

            _mouseDown = true;
            _lastMouseActivity = DateTime.UtcNow;
            _wasPinching = false;
            CameraHost.CaptureMouse();
            HandleGameCursor(p, pinching: true);
        }

        private void CameraHost_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isGameMode) return;
            if (MouseToCanvas(e) is not { } p) return;

            _lastMouseActivity = DateTime.UtcNow;
            if (_mouseDown)
            {
                HandleGameCursor(p, pinching: true);
            }
            else if (!_game.IsSolved)
            {
                _gameCursor = p;
                DrawingSurface.InvalidateVisual();
            }
        }

        private void CameraHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_mouseDown) return;

            _mouseDown = false;
            CameraHost.ReleaseMouseCapture();
            HandleGameCursor(MouseToCanvas(e) ?? _lastHeldCursor, pinching: false);
        }

        private void CameraHost_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_mouseDown || !_isGameMode) return;
            _gameCursor = null;
            DrawingSurface.InvalidateVisual();
        }

        // Mode switching (Draw / Tower Game)

        private void DrawModeButton_Click(object sender, RoutedEventArgs e) => SetMode(game: false);

        private void GameModeButton_Click(object sender, RoutedEventArgs e) => SetMode(game: true);

        private void SetMode(bool game)
        {
            if (_isGameMode != game)
            {
                // Finish whatever the old mode was in the middle of.
                if (_isDrawingStrokeActive)
                {
                    _canvasManager.EndStroke();
                    _isDrawingStrokeActive = false;
                }
                if (_game.HeldDisc != null) _game.CancelHold();

                _isGameMode = game;

                _gestureRecognizer.ResetClearTimer();
                _pinch.Reset();
                _emaSmoother.Reset();
                _gameSmoother.Reset();
                _wasPinching = false;
                _mouseDown = false;
                _gameCursor = null;
                _handLostSince = null;

                DrawControlsPanel.Visibility = game ? Visibility.Collapsed : Visibility.Visible;
                GameControlsPanel.Visibility = game ? Visibility.Visible : Visibility.Collapsed;
                ClearProgressBar.Value = 0;
                GestureStatusText.Text = "None";
                FooterStatusText.Text = game
                    ? "Tower game: pinch your thumb and index finger over a tower to lift a disc."
                    : "Draw mode: raise your index finger to draw.";
            }

            UpdateModeButtons();
            DrawingSurface.InvalidateVisual();
        }

        private void UpdateModeButtons()
        {
            var active = new SolidColorBrush(Color.FromRgb(58, 123, 213));

            if (_isGameMode)
            {
                DrawModeButton.ClearValue(Control.BackgroundProperty);
                DrawModeButton.ClearValue(Control.ForegroundProperty);
                GameModeButton.Background = active;
                GameModeButton.Foreground = Brushes.White;
            }
            else
            {
                GameModeButton.ClearValue(Control.BackgroundProperty);
                GameModeButton.ClearValue(Control.ForegroundProperty);
                DrawModeButton.Background = active;
                DrawModeButton.Foreground = Brushes.White;
            }
        }

        private void Level1Button_Click(object sender, RoutedEventArgs e) => StartGameLevel(0);

        private void Level2Button_Click(object sender, RoutedEventArgs e) => StartGameLevel(1);

        private void RestartLevelButton_Click(object sender, RoutedEventArgs e) => StartGameLevel(_game.LevelIndex);

        private void StartGameLevel(int levelIndex)
        {
            _game.StartLevel(levelIndex);
            _wasPinching = false;
            _mouseDown = false;
            _gestureRecognizer.ResetClearTimer();
            ClearProgressBar.Value = 0;
            DrawingSurface.InvalidateVisual();
        }

        // Skia rendering

        private void DrawingSurface_OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            _canvasPixelWidth = e.Info.Width;
            _canvasPixelHeight = e.Info.Height;

            if (_isGameMode)
            {
                double holdProgress = _game.IsSolved ? ClearProgressBar.Value : 0;
                HanoiRenderer.Render(e.Surface.Canvas, e.Info.Width, e.Info.Height,
                                     _game, _gameCursor, _wasPinching, holdProgress);
                return;
            }

            _canvasManager.Render(e.Surface.Canvas, background: null); // transparent overlay
        }

        // Toolbar handlers

        private void ColorSwatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string colorName })
            {
                var mediaColor = (Color)ColorConverter.ConvertFromString(colorName)!;
                _canvasManager.StrokeColor = new SKColor(mediaColor.R, mediaColor.G, mediaColor.B);
                _canvasManager.EraserMode = false;
                EraserCheckBox.IsChecked = false;
            }
        }

        private void ThicknessSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _canvasManager.StrokeThickness = (float)e.NewValue;
        }

        private void EraserCheckBox_OnChanged(object sender, RoutedEventArgs e)
        {
            _canvasManager.EraserMode = EraserCheckBox.IsChecked == true;
        }
        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.Clear();
            DrawingSurface.InvalidateVisual();
        }

        private void SavePngButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "PNG Image (*.png)|*.png",
                FileName = "AndreasDrawing.png"
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    ExportService.SavePng(_canvasManager, _canvasPixelWidth, _canvasPixelHeight, dialog.FileName);
                    FooterStatusText.Text = $"Saved PNG to {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to save PNG: {ex.Message}", "Export Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SavePdfButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "PDF Document (*.pdf)|*.pdf",
                FileName = "AndreasDrawing.pdf"
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    ExportService.SavePdf(_canvasManager, _canvasPixelWidth, _canvasPixelHeight, dialog.FileName);
                    FooterStatusText.Text = $"Saved PDF to {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to save PDF: {ex.Message}", "Export Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
