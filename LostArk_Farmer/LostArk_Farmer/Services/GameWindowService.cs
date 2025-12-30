using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace LostArkAutoPlayer.Services
{
    public class GameWindowService
    {
        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_ACTIVATE = 0x0006;
        private static readonly IntPtr WA_ACTIVE = new IntPtr(1);
        private const string PROCESS_NAME = "LOSTARK";

        public IntPtr GameHandle { get; private set; }

        public async Task WaitForGameProcessAsync()
        {
            Process gameProcess = null;
            while (gameProcess == null)
            {
                var procs = Process.GetProcessesByName(PROCESS_NAME);
                if (procs.Length > 0)
                {
                    gameProcess = procs[0];
                    GameHandle = gameProcess.MainWindowHandle;
                    Console.WriteLine($">> 鎖定視窗: {gameProcess.MainWindowTitle}");
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