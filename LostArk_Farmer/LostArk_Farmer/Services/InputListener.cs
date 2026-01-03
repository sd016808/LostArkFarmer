using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace LostArkAutoPlayer.Services
{
    public class InputListener
    {
        // 匯入 Windows API 來讀取鍵盤狀態
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        // 虛擬鍵碼 (Virtual Key Codes)
        private const int VK_F7 = 0x76;
        private const int VK_F8 = 0x77;
        private const int VK_F9 = 0x78;
        private const int VK_F10 = 0x79;

        // 事件定義
        public event EventHandler OnToggleOverlayRequested; // F7
        public event EventHandler OnSetOriginRequested;     // F8
        public event EventHandler OnStartRequested;         // F9
        public event EventHandler OnStopRequested;          // F10

        // 狀態旗標：記錄該按鍵是否「曾經被按下，且尚未放開」
        private bool _isF7Down = false;
        private bool _isF8Down = false;
        private bool _isF9Down = false;
        private bool _isF10Down = false;

        public void StartListening()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    CheckKey(VK_F7, ref _isF7Down, OnToggleOverlayRequested);
                    CheckKey(VK_F8, ref _isF8Down, OnSetOriginRequested);
                    CheckKey(VK_F9, ref _isF9Down, OnStartRequested);
                    CheckKey(VK_F10, ref _isF10Down, OnStopRequested);

                    // 降低 CPU 使用率，每 10ms 檢查一次
                    await Task.Delay(10);
                }
            });
        }

        /// <summary>
        /// 檢查按鍵狀態，並在「放開瞬間」觸發事件
        /// </summary>
        /// <param name="key">虛擬鍵碼</param>
        /// <param name="wasDown">該按鍵的上一次狀態旗標 (ref)</param>
        /// <param name="handler">要觸發的事件</param>
        private void CheckKey(int key, ref bool wasDown, EventHandler handler)
        {
            // 檢查當前按鍵是否按下 (最高位為 1 代表按下)
            bool isDownNow = (GetAsyncKeyState(key) & 0x8000) != 0;

            if (isDownNow)
            {
                // 1. 如果現在是按下的，標記為 true
                wasDown = true;
            }
            else
            {
                // 2. 如果現在是放開的，且之前是按下的 (wasDown == true)
                //    代表這是一個「放開瞬間 (Key Up)」
                if (wasDown)
                {
                    handler?.Invoke(this, EventArgs.Empty);
                    wasDown = false; // 重置狀態，等待下次按下
                }
            }
        }
    }
}