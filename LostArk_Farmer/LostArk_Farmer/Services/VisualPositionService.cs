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
        // ==========================================
        // [參數設定區] - 請修改這裡
        // ==========================================

        // 2. 顏色範圍 (維持不變)
        private readonly Scalar _lowerGreen = new Scalar(35, 40, 60);
        private readonly Scalar _upperGreen = new Scalar(95, 255, 255);

        // 3. ROI 裁切 (維持不變)
        private const double IGNORE_TOP_RATIO = 0.15;
        private const double IGNORE_BOTTOM_RATIO = 0.15;
        private const double IGNORE_LEFT_RATIO = 0.20;
        private const double IGNORE_RIGHT_RATIO = 0.20;

        // 4. 移動參數 [關鍵修改點]
        private const double _minContourArea = 30;
        private const short MAX_STICK_VALUE = 32767;

        // [修改] 容許誤差：從 30 改為 10
        // 這樣距離 26 的時候，程式就不會偷懶，會繼續往右推
        private const int MOVEMENT_THRESHOLD = 10;

        private const int SLOW_DOWN_DISTANCE = 200;

        // [修改] 最小力度：從 0.6 改為 0.75
        // 因為現在只剩下最後幾步路，如果推太小力 (例如 40%) 遊戲可能會判定為 Deadzone 而不動
        // 改成 0.75 保證它會「用力」把最後這 26 像素走完
        private const double MIN_STICK_STRENGTH = 0.75;

        // 5. 除錯設定
        private const bool ENABLE_DEBUG_SAVE = false;

        private string _debugFolderPath;
        private OpenCvSharp.Point? _targetPosition = null;

        public VisualPositionService()
        {
            _debugFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DebugImgs");
            if (!Directory.Exists(_debugFolderPath)) Directory.CreateDirectory(_debugFolderPath);
        }

        public bool IsTargetSet => _targetPosition.HasValue;

        public bool SetCurrentAsTarget(Bitmap screenshot)
        {
            var result = GetPositionAndSaveDebug(screenshot, "SetOrigin");
            if (result.Center.HasValue)
            {
                _targetPosition = result.Center.Value;
                return true;
            }
            return false;
        }

        public void ResetTarget() => _targetPosition = null;

        public (short stickX, short stickY, double distance) CalculateCorrectionVector(Bitmap screenshot)
        {
            if (!_targetPosition.HasValue) return (0, 0, 0);

            var result = GetPositionAndSaveDebug(screenshot, "Correction");
            if (!result.Center.HasValue) return (0, 0, 0);

            // 1. 計算座標差異
            OpenCvSharp.Point current = result.Center.Value;
            OpenCvSharp.Point target = _targetPosition.Value;

            double dx = target.X - current.X;
            double dy = target.Y - current.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            string dirH = Math.Abs(dx) < 10 ? "-" : (dx > 0 ? "[右] ->" : "[左] <-");
            string dirV = Math.Abs(dy) < 10 ? "-" : (dy > 0 ? "[下] v" : "[上] ^");
            Console.WriteLine($"[Pos] Dist:{distance:F0} | {dirH} {dirV}");

            // 這裡就是關鍵：如果 Distance(26) < Threshold(10) 才會停。
            // 現在改成 10，所以 26 > 10，它會繼續往下執行移動邏輯。
            if (distance < MOVEMENT_THRESHOLD) return (0, 0, distance);

            // 2. 角度計算
            double calcDx = dx;
            double calcDy = -dy;
            double angle = Math.Atan2(calcDy, calcDx);

            // 3. 力度計算
            double strengthRatio = Math.Min(distance / SLOW_DOWN_DISTANCE, 1.0);
            double finalStrength = Math.Max(strengthRatio, MIN_STICK_STRENGTH) * MAX_STICK_VALUE;

            short stickX = (short)(Math.Cos(angle) * finalStrength);
            short stickY = (short)(Math.Sin(angle) * finalStrength);

            return (stickX, stickY, distance);
        }

        private (OpenCvSharp.Point? Center, OpenCvSharp.Rect BoundingBox) GetPositionAndSaveDebug(Bitmap screenshot, string prefix)
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

                    if (ENABLE_DEBUG_SAVE)
                    {
                        using (var debugImg = src.Clone())
                        {
                            Cv2.Rectangle(debugImg, roiRect, Scalar.Yellow, 2);
                            if (finalCenter.HasValue)
                            {
                                Cv2.Rectangle(debugImg, finalRect, Scalar.Red, 1);
                                Cv2.Circle(debugImg, finalCenter.Value, (int)circleRadius, Scalar.Green, 2);
                                Cv2.DrawMarker(debugImg, finalCenter.Value, Scalar.Red, MarkerTypes.Cross, 20, 2);
                            }
                            if (_targetPosition.HasValue)
                            {
                                Cv2.Circle(debugImg, _targetPosition.Value, 5, Scalar.Blue, -1);
                                Cv2.PutText(debugImg, "ORIGIN", _targetPosition.Value, HersheyFonts.HersheySimplex, 0.5, Scalar.Blue, 1);
                                if (finalCenter.HasValue)
                                {
                                    Cv2.Line(debugImg, finalCenter.Value, _targetPosition.Value, Scalar.Magenta, 2);
                                }
                            }
                            string filename = Path.Combine(_debugFolderPath, $"{prefix}_{DateTime.Now.Ticks}.png");
                            debugImg.SaveImage(filename);
                        }
                    }
                    return (finalCenter, finalRect);
                }
            }
        }

        public OpenCvSharp.Rect GetDetectedRect(Bitmap screenshot)
        {
            var result = GetPositionAndSaveDebug(screenshot, "DebugView");
            return result.BoundingBox;
        }

        public void Dispose() { }
    }
}