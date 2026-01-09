using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LostArkAutoPlayer.Services
{
    public class VirtualControllerService : IDisposable
    {
        private ViGEmClient _client;
        private IXbox360Controller _controller;

        // 映射表
        private static readonly Dictionary<string, Xbox360Button> _buttonMap
            = new Dictionary<string, Xbox360Button>(StringComparer.OrdinalIgnoreCase)
        {
            { "A", Xbox360Button.A }, { "B", Xbox360Button.B }, { "X", Xbox360Button.X }, { "Y", Xbox360Button.Y },
            { "LB", Xbox360Button.LeftShoulder }, { "RB", Xbox360Button.RightShoulder },
            { "LT_BTN", Xbox360Button.LeftThumb }, { "RT_BTN", Xbox360Button.RightThumb },
            { "Start", Xbox360Button.Start }, { "Back", Xbox360Button.Back }
        };

        // 注意：方法簽章改成 async Task
        public async Task InitializeAsync()
        {
            int maxRetries = 3;
            int delayMs = 1000;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    Console.WriteLine($"[{i + 1}/{maxRetries}] 初始化虛擬手把驅動 (ViGEm)...");

                    Cleanup(); // 確保乾淨的狀態

                    // ViGEm 的初始化通常不是 async 的，若會卡住 UI，可以包在 Task.Run 裡
                    await Task.Run(() =>
                    {
                        _client = new ViGEmClient();
                        _controller = _client.CreateXbox360Controller();
                        _controller.AutoSubmitReport = true;
                        _controller.Connect();
                    });

                    Console.WriteLine(">> 虛擬手把已連線成功。");
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Warning] 初始化失敗: {ex.Message}");

                    if (i == maxRetries - 1)
                    {
                        Console.WriteLine("[Fatal] 無法連接虛擬手把驅動。");
                        throw;
                    }

                    // 非阻塞式等待
                    await Task.Delay(delayMs);
                }
            }
        }

        // 輔助方法：確保釋放資源
        private void Cleanup()
        {
            try
            {
                _controller?.Disconnect();
                _client?.Dispose();
            }
            catch { /* 忽略釋放過程的錯誤 */ }
            finally
            {
                _controller = null;
                _client = null;
            }
        }

        // ========================================================================
        // [新增] 支援類比搖桿的精準控制 (給 VisualPositionService 使用)
        // ========================================================================
        public void SetLeftStick(short x, short y)
        {
            if (_controller == null) return;
            try
            {
                _controller.SetAxisValue(Xbox360Axis.LeftThumbX, x);
                _controller.SetAxisValue(Xbox360Axis.LeftThumbY, y);
            }
            catch { /* 忽略連線錯誤 */ }
        }
        // ========================================================================

        public void SendInput(string key, bool isDown)
        {
            if (_controller == null || string.IsNullOrWhiteSpace(key)) return;
            key = key.Trim().ToUpper();

            try
            {
                // 1. 處理搖桿 (Axis) - 這是給腳本用的全速移動
                switch (key)
                {
                    case "UP": _controller.SetAxisValue(Xbox360Axis.LeftThumbY, isDown ? (short)32767 : (short)0); return;
                    case "DOWN": _controller.SetAxisValue(Xbox360Axis.LeftThumbY, isDown ? (short)-32768 : (short)0); return;
                    case "LEFT": _controller.SetAxisValue(Xbox360Axis.LeftThumbX, isDown ? (short)-32768 : (short)0); return;
                    case "RIGHT": _controller.SetAxisValue(Xbox360Axis.LeftThumbX, isDown ? (short)32767 : (short)0); return;
                }

                // 2. 處理板機 (Trigger)
                if (key == "LT") { _controller.SetSliderValue(Xbox360Slider.LeftTrigger, isDown ? (byte)255 : (byte)0); return; }
                if (key == "RT") { _controller.SetSliderValue(Xbox360Slider.RightTrigger, isDown ? (byte)255 : (byte)0); return; }

                // 3. 處理按鈕 (Buttons)
                if (_buttonMap.ContainsKey(key))
                {
                    _controller.SetButtonState(_buttonMap[key], isDown);
                }
            }
            catch { /* 忽略微小的輸入錯誤，避免中斷腳本 */ }
        }

        public void ResetAllInputs()
        {
            if (_controller == null) return;
            try
            {
                // 歸零所有輸入
                _controller.SetAxisValue(Xbox360Axis.LeftThumbX, 0);
                _controller.SetAxisValue(Xbox360Axis.LeftThumbY, 0);
                _controller.SetAxisValue(Xbox360Axis.RightThumbX, 0);
                _controller.SetAxisValue(Xbox360Axis.RightThumbY, 0);
                _controller.SetSliderValue(Xbox360Slider.LeftTrigger, 0);
                _controller.SetSliderValue(Xbox360Slider.RightTrigger, 0);

                foreach (var btn in _buttonMap.Values)
                {
                    _controller.SetButtonState(btn, false);
                }
                // 強制送出報告確保歸零生效 (雖然 AutoSubmitReport=true，但在 Reset 時明確呼叫較保險)
                _controller.SubmitReport();
            }
            catch { /* 忽略連線錯誤 */ }
        }

        public void Dispose()
        {
            ResetAllInputs();
            if (_controller != null)
            {
                try { _controller.Disconnect(); } catch { }
                _controller = null;
            }
            if (_client != null)
            {
                try { _client.Dispose(); } catch { }
                _client = null;
            }
        }
    }
}