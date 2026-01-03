using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace LostArkAutoPlayer.Services
{
    public class VisualPositionService : IDisposable
    {
        public event Action<Bitmap> OnDebugImageReady;

        // ==========================================
        // [參數設定區]
        // ==========================================

        // 1. 狀態文字位置 (百分比)
        private const double TEXT_X_RATIO = 0.30; // 靠右 30%
        private const double TEXT_Y_RATIO = 0.05; // 靠上 5%
        private const double LINE_SPACING_RATIO = 0.04;

        // 2. 顏色與 ROI
        private readonly Scalar _lowerGreen = new Scalar(35, 40, 60);
        private readonly Scalar _upperGreen = new Scalar(95, 255, 255);

        private const double IGNORE_TOP_RATIO = 0.15;
        private const double IGNORE_BOTTOM_RATIO = 0.15;
        private const double IGNORE_LEFT_RATIO = 0.20;
        private const double IGNORE_RIGHT_RATIO = 0.20;

        private const double _minContourArea = 30;
        private const short MAX_STICK_VALUE = 32767;
        private const int MOVEMENT_THRESHOLD = 10;
        private const int SLOW_DOWN_DISTANCE = 200;
        private const double MIN_STICK_STRENGTH = 0.75;

        private const bool ENABLE_DEBUG_SAVE = false;
        private string _debugFolderPath;
        private OpenCvSharp.Point? _targetPosition = null;

        // ★ 新增：F7 Overlay 開關狀態
        public bool IsOverlayEnabled { get; set; } = true;
        public bool IsScriptRunning { get; set; } = false;

        public VisualPositionService()
        {
            _debugFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DebugImgs");
            if (!Directory.Exists(_debugFolderPath)) Directory.CreateDirectory(_debugFolderPath);
        }

        public bool IsTargetSet => _targetPosition.HasValue;

        public bool SetCurrentAsTarget(Bitmap screenshot)
        {
            var result = GetPositionAndProcessDebug(screenshot, "SetOrigin");
            if (result.Center.HasValue)
            {
                _targetPosition = result.Center.Value;
                return true;
            }
            return false;
        }

        public void ResetTarget()
        {
            _targetPosition = null;
            OnDebugImageReady?.Invoke(null);
        }

        public (short stickX, short stickY, double distance) CalculateCorrectionVector(Bitmap screenshot)
        {
            // 只要 "沒設定原點 (F8 OFF)" 或者 "腳本沒跑 (F9 OFF)"
            // -> 強制只顯示文字
            if (!_targetPosition.HasValue || !IsScriptRunning)
            {
                DrawOnlyStatus(screenshot);
                return (0, 0, 0);
            }

            // F8+F9 都開啟，才進行偵測
            var result = GetPositionAndProcessDebug(screenshot, "Correction");
            if (!result.Center.HasValue) return (0, 0, 0);

            OpenCvSharp.Point current = result.Center.Value;
            OpenCvSharp.Point target = _targetPosition.Value;

            double dx = target.X - current.X;
            double dy = target.Y - current.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            if (distance < MOVEMENT_THRESHOLD) return (0, 0, distance);

            double calcDx = dx;
            double calcDy = -dy;
            double angle = Math.Atan2(calcDy, calcDx);

            double strengthRatio = Math.Min(distance / SLOW_DOWN_DISTANCE, 1.0);
            double finalStrength = Math.Max(strengthRatio, MIN_STICK_STRENGTH) * MAX_STICK_VALUE;

            short stickX = (short)(Math.Cos(angle) * finalStrength);
            short stickY = (short)(Math.Sin(angle) * finalStrength);

            return (stickX, stickY, distance);
        }

        private void DrawOnlyStatus(Bitmap screenshot)
        {
            using (var src = BitmapConverter.ToMat(screenshot))
            using (var debugImg = new Mat(src.Size(), MatType.CV_8UC3, Scalar.All(0)))
            {
                DrawStatusText(debugImg);

                var bmp = BitmapConverter.ToBitmap(debugImg);
                OnDebugImageReady?.Invoke(bmp);
            }
        }

        private (OpenCvSharp.Point? Center, OpenCvSharp.Rect BoundingBox) GetPositionAndProcessDebug(Bitmap screenshot, string prefix)
        {
            using (var src = BitmapConverter.ToMat(screenshot))
            {
                int ignoreTop = (int)(src.Height * IGNORE_TOP_RATIO);
                int ignoreBottom = (int)(src.Height * IGNORE_BOTTOM_RATIO);
                int roiHeight = src.Height - ignoreTop - ignoreBottom;
                int ignoreLeft = (int)(src.Width * IGNORE_LEFT_RATIO);
                int ignoreRight = (int)(src.Width * IGNORE_RIGHT_RATIO);
                int roiWidth = src.Width - ignoreLeft - ignoreRight;

                OpenCvSharp.Rect roiRect;
                if (roiHeight > 0 && roiWidth > 0)
                    roiRect = new OpenCvSharp.Rect(ignoreLeft, ignoreTop, roiWidth, roiHeight);
                else
                    roiRect = new OpenCvSharp.Rect(0, 0, src.Width, src.Height);

                using (var roiMat = new Mat(src, roiRect))
                using (var hsv = new Mat())
                using (var mask = new Mat())
                {
                    Cv2.CvtColor(roiMat, hsv, ColorConversionCodes.BGR2HSV);
                    Cv2.InRange(hsv, _lowerGreen, _upperGreen, mask);

                    using (var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(5, 5)))
                    {
                        Cv2.Dilate(mask, mask, kernel);
                    }

                    OpenCvSharp.Point[][] contours;
                    HierarchyIndex[] hierarchy;
                    Cv2.FindContours(mask, out contours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                    OpenCvSharp.Point? finalCenter = null;
                    OpenCvSharp.Rect finalRect = new OpenCvSharp.Rect(0, 0, 0, 0);
                    Point2f circleCenter = new Point2f();
                    float circleRadius = 0;
                    double maxArea = 0;

                    foreach (var contour in contours)
                    {
                        double area = Cv2.ContourArea(contour);
                        if (area > _minContourArea && area > maxArea)
                        {
                            maxArea = area;
                            Cv2.MinEnclosingCircle(contour, out circleCenter, out circleRadius);
                            var relativeRect = Cv2.BoundingRect(contour);
                            finalRect = new OpenCvSharp.Rect(relativeRect.X + roiRect.X, relativeRect.Y + roiRect.Y, relativeRect.Width, relativeRect.Height);
                        }
                    }

                    if (maxArea > 0)
                    {
                        finalCenter = new OpenCvSharp.Point((int)(circleCenter.X + roiRect.X), (int)(circleCenter.Y + roiRect.Y));
                    }

                    // 繪圖
                    using (var debugImg = new Mat(src.Size(), MatType.CV_8UC3, Scalar.All(0)))
                    {
                        // ★ 若 F7 開啟，才畫框線與圓圈
                        if (IsOverlayEnabled && _targetPosition.HasValue)
                        {
                            if (finalCenter.HasValue)
                            {
                                Cv2.Rectangle(debugImg, finalRect, Scalar.Red, 1);
                                Cv2.Circle(debugImg, finalCenter.Value, (int)circleRadius, Scalar.Green, 2);
                                Cv2.DrawMarker(debugImg, finalCenter.Value, Scalar.Red, MarkerTypes.Cross, 20, 2);
                            }

                            Cv2.Circle(debugImg, _targetPosition.Value, 5, Scalar.Blue, -1);
                            Cv2.PutText(debugImg, "ORIGIN", _targetPosition.Value, HersheyFonts.HersheySimplex, 0.5, Scalar.Blue, 1);

                            if (finalCenter.HasValue)
                            {
                                Cv2.Line(debugImg, finalCenter.Value, _targetPosition.Value, Scalar.Magenta, 2);
                            }
                        }

                        // 無論 F7 是否開啟，狀態文字都顯示
                        DrawStatusText(debugImg);

                        var bitmapForOverlay = BitmapConverter.ToBitmap(debugImg);
                        OnDebugImageReady?.Invoke(bitmapForOverlay);

                        if (ENABLE_DEBUG_SAVE)
                        {
                            string filename = Path.Combine(_debugFolderPath, $"{prefix}_{DateTime.Now.Ticks}.png");
                            debugImg.SaveImage(filename);
                        }
                    }

                    return (finalCenter, finalRect);
                }
            }
        }

        private void DrawStatusText(Mat img)
        {
            // 計算間距高度
            int spacing = (int)(img.Height * LINE_SPACING_RATIO);
            if (spacing < 30) spacing = 30; // 最小間距保護

            // 計算 Y 座標
            int x = (int)(img.Width * TEXT_X_RATIO);
            int yScript = (int)(img.Height * TEXT_Y_RATIO);
            int yReturn = yScript + spacing;
            int yOverlay = yReturn + spacing; // ★ 第三行位置

            var posScript = new OpenCvSharp.Point(x, yScript);
            var posReturn = new OpenCvSharp.Point(x, yReturn);
            var posOverlay = new OpenCvSharp.Point(x, yOverlay); // ★ 第三行座標點

            // 1. F9 Script Status
            string scriptTxt = IsScriptRunning ? "[F9/F10] SCRIPT: [ON]" : "[F9/F10] SCRIPT: [OFF]";
            Scalar scriptCol = IsScriptRunning ? Scalar.Lime : Scalar.Red;
            Cv2.PutText(img, scriptTxt, posScript, HersheyFonts.HersheySimplex, 1.0, scriptCol, 2);

            // 2. F8 Auto Return Status
            string returnTxt = _targetPosition.HasValue ? "[F8] AUTO RETURN: [ON]" : "[F8] AUTO RETURN: [OFF]";
            Scalar returnCol = _targetPosition.HasValue ? Scalar.Cyan : Scalar.Red;
            Cv2.PutText(img, returnTxt, posReturn, HersheyFonts.HersheySimplex, 1.0, returnCol, 2);

            // 3. F7 Overlay Status (★ 新增的顯示邏輯)
            string overlayTxt = IsOverlayEnabled ? "[F7] OVERLAY: [ON]" : "[F7] OVERLAY: [OFF]";
            // ON 用綠色，OFF 用紅色
            Scalar overlayCol = IsOverlayEnabled ? Scalar.Lime : Scalar.Red;
            Cv2.PutText(img, overlayTxt, posOverlay, HersheyFonts.HersheySimplex, 1.0, overlayCol, 2);
        }

        public void Dispose() { }
    }
}