using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace LostArkAutoPlayer.Services
{
    public class GameWindowService
    {
        // === Win32 API 匯入區 ===

        // [新增] 告訴 Windows 這個程式支援高解析度，不要偷縮放
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const uint WM_ACTIVATE = 0x0006;
        private static readonly IntPtr WA_ACTIVE = new IntPtr(1);
        private const string PROCESS_NAME = "LOSTARK";

        public IntPtr GameHandle { get; private set; } = IntPtr.Zero;

        // 建構子：程式一啟動這個服務時，就宣告 DPI Aware
        public GameWindowService()
        {
            try
            {
                // 這行最關鍵！呼叫後 GetWindowRect 就會回傳 2560x1440 了
                SetProcessDPIAware();
            }
            catch (Exception)
            {
                // 某些舊版 Windows 可能不支援，通常可忽略
            }
        }

        public Rectangle GetGameWindowBounds()
        {
            if (GameHandle == IntPtr.Zero)
            {
                return Rectangle.Empty;
            }

            RECT rect;
            if (GetWindowRect(GameHandle, out rect))
            {
                // [修正] 把寫死的 2560 改回動態計算
                // 因為加了 SetProcessDPIAware()，這裡算出來就會是正確的 2560x1440
                return new Rectangle(
                    rect.Left,
                    rect.Top,
                    rect.Right - rect.Left, // 寬度
                    rect.Bottom - rect.Top  // 高度
                );
            }

            return Rectangle.Empty;
        }

        public async Task WaitForGameProcessAsync()
        {
            Process gameProcess = null;
            while (gameProcess == null)
            {
                var procs = Process.GetProcessesByName(PROCESS_NAME);
                if (procs.Length > 0)
                {
                    gameProcess = procs[0];
                    if (gameProcess.MainWindowHandle != IntPtr.Zero)
                    {
                        GameHandle = gameProcess.MainWindowHandle;
                        Console.WriteLine($">> 鎖定視窗: {gameProcess.MainWindowTitle}");
                    }
                    else
                    {
                        gameProcess = null;
                        Console.WriteLine($"等待 {PROCESS_NAME} 視窗建立...");
                        await Task.Delay(1000);
                    }
                }
                else
                {
                    Console.WriteLine($"等待 {PROCESS_NAME} 啟動...");
                    await Task.Delay(3000);
                }
            }
        }

        public void KeepAwake()
        {
            if (GameHandle != IntPtr.Zero)
            {
                PostMessage(GameHandle, WM_ACTIVATE, WA_ACTIVE, IntPtr.Zero);
            }
        }
    }
}