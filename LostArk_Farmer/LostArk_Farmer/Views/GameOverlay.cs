using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LostArkAutoPlayer.Services
{
    public partial class GameOverlay : Form
    {
        private PictureBox _pictureBox;

        // --- Win32 API 穿透與置頂 ---
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOPMOST = 0x0008;

        public GameOverlay()
        {
            // 1. 視窗基礎設定
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(0, 0);

            // 2. 關鍵修正：關閉 DPI 自動縮放
            this.AutoScaleMode = AutoScaleMode.None;

            // 3. ★關鍵修正：改回黑色去背
            // 你的 Debug 圖片背景如果是透明的 (ARGB 0,0,0,0)，在 WinForm 裡會被視為黑色
            // 所以必須把去背色設為黑色，才能讓它變透明
            this.BackColor = Color.Black;
            this.TransparencyKey = Color.Black;

            // 4. 初始化
            this.DoubleBuffered = true;

            _pictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent, // PictureBox 也要透明
                SizeMode = PictureBoxSizeMode.Normal
            };
            this.Controls.Add(_pictureBox);

            // 5. 載入時開啟滑鼠穿透
            this.Load += (s, e) => EnableClickThrough();
        }

        private void EnableClickThrough()
        {
            try
            {
                int initialStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);
                // 加上 穿透(TRANSPARENT) 和 Layered 屬性
                SetWindowLong(this.Handle, GWL_EXSTYLE, initialStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST);
            }
            catch { }
        }

        public void UpdateImage(Bitmap newImage)
        {
            if (this.IsDisposed || _pictureBox.IsDisposed) return;

            if (this.InvokeRequired)
            {
                try { this.Invoke(new Action(() => UpdateImage(newImage))); }
                catch { }
                return;
            }

            if (newImage == null)
            {
                _pictureBox.Image = null;
                return;
            }

            // 確保視窗大小跟圖片一樣大
            if (this.Size != newImage.Size)
            {
                this.Size = newImage.Size;
            }

            // 更新圖片 (Clone 以防來源被 Dispose)
            var oldImage = _pictureBox.Image;
            try
            {
                _pictureBox.Image = (Image)newImage.Clone();
            }
            catch
            {
                // 容錯：如果 Clone 失敗 (例如 newImage 壞了)，就忽略這次更新
            }

            oldImage?.Dispose();
        }
    }
}