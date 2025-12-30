using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LostArkAutoPlayer.Services
{
    public class InputListener
    {
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        private const int VK_F9 = 0x78;  // Start
        private const int VK_F10 = 0x79; // Stop

        public event EventHandler OnStartRequested;
        public event EventHandler OnStopRequested;

        public void StartListening()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    if ((GetAsyncKeyState(VK_F9) & 0x8000) != 0)
                    {
                        OnStartRequested?.Invoke(this, EventArgs.Empty);
                        await Task.Delay(500); // Debounce
                    }
                    if ((GetAsyncKeyState(VK_F10) & 0x8000) != 0)
                    {
                        OnStopRequested?.Invoke(this, EventArgs.Empty);
                        await Task.Delay(500); // Debounce
                    }
                    await Task.Delay(10);
                }
            });
        }
    }
}