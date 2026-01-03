using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LostArkAutoPlayer.Services
{
    public static class ScreenCapture
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hDC, uint nFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // 關鍵參數：告訴 PrintWindow 我們要完整的渲染內容 (包含 DirectX)
        // 0x00000002 = PW_RENDERFULLCONTENT (Windows 8.1+)
        // 0x00000003 = PW_CLIENTONLY | PW_RENDERFULLCONTENT
        private const uint PW_RENDERFULLCONTENT = 0x00000002;
           
        /// <summary>
        /// 使用 PrintWindow 進行後台截圖
        /// </summary>
        /// <param name="handle">遊戲視窗的 Handle (GameHandle)</param>
        public static Bitmap CaptureWindow(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return null;

            // 1. 取得視窗大小
            RECT rect;
            if (!GetWindowRect(handle, out rect)) return null;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            if (width <= 0 || height <= 0) return null;

            // 2. 建立 Bitmap
            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);

            // 3. 建立 Graphics 物件以取得 HDC
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();

                try
                {
                    // 4. [關鍵] 呼叫 PrintWindow
                    // 使用 PW_RENDERFULLCONTENT 參數，這能讓大多數 DirectX 視窗在後台也能被截取
                    bool success = PrintWindow(handle, hdc, PW_RENDERFULLCONTENT);

                    // 如果失敗 (有些舊系統或特殊保護)，嘗試不帶參數的舊版
                    if (!success)
                    {
                        PrintWindow(handle, hdc, 0);
                    }
                }
                finally
                {
                    // 釋放 HDC，非常重要
                    g.ReleaseHdc(hdc);
                }
            }

            return bmp;
        }
    }
}