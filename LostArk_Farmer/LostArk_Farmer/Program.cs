using LostArkAutoPlayer.Models;
using LostArkAutoPlayer.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LostArkAutoPlayer
{
    class Program
    {
        const string VERSION = "1.0.6";
        const string CONFIG_PATH = "skill_config.json";

        static volatile bool _isRunning = false;
        static volatile bool _isOverlayVisible = true;
        static CancellationTokenSource _cts;

        static VirtualControllerService _controllerService;
        static GameWindowService _windowService;
        static VisualPositionService _visualPosService;
        static GameOverlay _overlay;

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.Title = $"LostArkFarmer v{VERSION}";

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"=== Lost Ark Farmer v{VERSION} ===");
            Console.WriteLine("模式: 大地圖 (Tab) 綠箭頭");
            Console.ResetColor();

            var updateService = new UpdateService(VERSION, "sd016808", "LostArkFarmer");

            // 1. 檢查更新
            await updateService.CheckForUpdatesAsync();

            // 1. 啟動 Overlay (UI Thread)
            Thread uiThread = new Thread(() =>
            {
                _overlay = new GameOverlay();
                Application.Run(_overlay);
            });
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.IsBackground = true;
            uiThread.Start();
            await Task.Delay(1000);

            // 2. 初始化 Services
            _windowService = new GameWindowService();
            _controllerService = new VirtualControllerService();
            _visualPosService = new VisualPositionService();
            var inputListener = new InputListener();

            // 3. 連接圖片 (Overlay 更新事件)
            _visualPosService.OnDebugImageReady += (bmp) =>
            {
                if (_overlay != null && !_overlay.IsDisposed)
                {
                    if (_isOverlayVisible)
                        _overlay.UpdateImage(bmp);
                    else
                    {
                        _overlay.UpdateImage(null);
                        bmp?.Dispose();
                    }
                }
            };

            // 4. 載入設定檔
            if (!File.Exists(CONFIG_PATH)) CreateDefaultProfile();
            ScriptConfig config;
            try
            {
                config = JsonConvert.DeserializeObject<ScriptConfig>(File.ReadAllText(CONFIG_PATH));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"設定檔讀取錯誤: {ex.Message}");
                return;
            }

            // 初始化控制器與視窗
            await _controllerService.InitializeAsync();
            await _windowService.WaitForGameProcessAsync();

            // 5. 事件綁定

            // F7: Overlay 開關
            inputListener.OnToggleOverlayRequested += (s, e) =>
            {
                _isOverlayVisible = !_isOverlayVisible;
                _visualPosService.IsOverlayEnabled = _isOverlayVisible;
                Console.WriteLine($"[Display] Overlay: {(_isOverlayVisible ? "ON" : "OFF")}");
                if (!_isOverlayVisible) _overlay.UpdateImage(null);
            };

            // F8: 定位 (原點) 開關
            inputListener.OnSetOriginRequested += (s, e) =>
            {
                if (_visualPosService.IsTargetSet)
                {
                    _visualPosService.ResetTarget();
                    Console.WriteLine("[Pos] 定位已關閉 (原點清除)");
                }
                else
                {
                    Console.WriteLine("[Pos] 定位已開啟 (設定原點...)");
                    CaptureAndSetOrigin();
                }
            };

            // F9: 啟動
            inputListener.OnStartRequested += (s, e) =>
            {
                if (!_isRunning)
                {
                    _isRunning = true;
                    _visualPosService.IsScriptRunning = true;
                    Console.WriteLine("\n>>> 腳本啟動 (F9) <<<");
                }
            };

            // F10: 停止
            inputListener.OnStopRequested += (s, e) =>
            {
                if (_isRunning)
                {
                    _isRunning = false;
                    _visualPosService.IsScriptRunning = false;
                    _cts?.Cancel();
                    Console.WriteLine("\n>>> 腳本停止 (F10) <<<");
                }
            };

            inputListener.StartListening();

            Console.WriteLine("[Hotkeys]");
            Console.WriteLine("  F7: Overlay 顯示/隱藏");
            Console.WriteLine("  F8: 原點定位 開啟/關閉");
            Console.WriteLine("  F9: 開始掛機");
            Console.WriteLine("  F10: 停止掛機");

            // ================================================================
            // ★ 新增：獨立渲染迴圈 (Render Loop) - 解決畫面更新延遲問題
            // ================================================================
            var renderTask = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        // 只有當 Overlay 開啟時才進行畫面分析與繪製
                        if (_isOverlayVisible)
                        {
                            IntPtr handle = _windowService.GameHandle;
                            if (handle != IntPtr.Zero)
                            {
                                using (var bmp = ScreenCapture.CaptureWindow(handle))
                                {
                                    if (bmp != null)
                                    {
                                        // 這行會觸發 OnDebugImageReady 更新 Overlay
                                        // 即使沒按 F9，這行也能讓 F8 的狀態文字即時顯示在畫面上
                                        _visualPosService.CalculateCorrectionVector(bmp);
                                    }
                                }
                            }
                        }
                        else
                        {
                            // 若 Overlay 關閉，降低檢查頻率以省電
                            await Task.Delay(500);
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        // 避免繪圖錯誤影響主程式
                        // Console.WriteLine($"[Render] {ex.Message}"); 
                    }

                    // ★ 關鍵設定：更新頻率控制
                    // 33ms 約等於 30 FPS，視覺流暢且不會造成 LAG
                    await Task.Delay(33);
                }
            });

            // ================================================================
            // 6. 主邏輯迴圈 (Logic Loop) - 專注於跑腳本
            // ================================================================
            while (true)
            {
                // 等待啟動
                if (!_isRunning)
                {
                    await Task.Delay(100);
                    continue;
                }

                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                try
                {
                    while (!token.IsCancellationRequested && _isRunning)
                    {
                        // --- 技能循環 ---
                        foreach (var step in config.Skills)
                        {
                            token.ThrowIfCancellationRequested();
                            _windowService.KeepAwake(); // 防止螢幕休眠

                            // 按下按鍵
                            if (step.Buttons != null)
                            {
                                foreach (var btn in step.Buttons)
                                {
                                    _controllerService.SendInput(btn, true);
                                    // 序列按鍵之間的微小延遲
                                    if (step.IsSequential && step.Buttons.Count > 1)
                                        await Task.Delay(200, token);
                                }
                            }

                            // 持續按壓時間 (PressDuration)
                            // 即使這裡 delay 500ms，上面的 Render Loop 依然在跑，所以畫面不會卡
                            await Task.Delay(step.PressDurationMs, token);

                            // 放開按鍵
                            if (step.Buttons != null)
                            {
                                for (int i = step.Buttons.Count - 1; i >= 0; i--)
                                {
                                    _controllerService.SendInput(step.Buttons[i], false);
                                }
                            }

                            // 技能冷卻 (CoolDown)
                            if (step.CoolDownMs > 0)
                                await Task.Delay(step.CoolDownMs, token);
                        }

                        // --- 自動回正邏輯 ---
                        // (需 F8 開啟且有原點)
                        await ExecuteReturnToOriginAsync(token);

                        // 迴圈之間的延遲
                        if (config.LoopDelayMs > 0)
                            await Task.Delay(config.LoopDelayMs, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // 正常停止
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] {ex.Message}");
                }
                finally
                {
                    // 確保停止時釋放所有按鍵
                    _controllerService.ResetAllInputs();
                    if (_cts != null) { _cts.Dispose(); _cts = null; }
                }
            }
        }

        // --- 輔助方法 ---

        static void CaptureAndSetOrigin()
        {
            var handle = _windowService.GameHandle;
            if (handle == IntPtr.Zero) return;
            using (var bmp = ScreenCapture.CaptureWindow(handle))
            {
                if (bmp != null)
                {
                    if (_visualPosService.SetCurrentAsTarget(bmp))
                        Console.WriteLine("[Pos] 原點已設定");
                    else
                        Console.WriteLine("[Pos Error] 沒抓到箭頭 (請開 Tab 大地圖)");
                }
            }
        }

        /// <summary>
        /// 執行回正動作
        /// </summary>
        static async Task ExecuteReturnToOriginAsync(CancellationToken token)
        {
            if (!_visualPosService.IsTargetSet) return;

            // 設定超時 (3 秒)，避免卡死
            var timeoutLimit = DateTime.Now.AddSeconds(3);
            bool isMoving = false;

            try
            {
                while (DateTime.Now < timeoutLimit && !token.IsCancellationRequested)
                {
                    IntPtr handle = _windowService.GameHandle;
                    if (handle == IntPtr.Zero) break;

                    // 這裡進行獨立截圖分析，確保邏輯判斷使用最新的畫面
                    // 雖然 Render Loop 也在截圖，但為了邏輯精確性，這裡分開截是比較保險的做法
                    using (var bmp = ScreenCapture.CaptureWindow(handle))
                    {
                        if (bmp == null)
                        {
                            Console.WriteLine("[Error] 無法截圖，跳過回正");
                            break;
                        }

                        // 1. 計算向量
                        var result = _visualPosService.CalculateCorrectionVector(bmp);
                        var stickX = result.stickX;
                        var stickY = result.stickY;
                        var distance = result.distance;

                        // 2. 判斷是否到達 (回傳 0 代表已在範圍內)
                        if (distance == 0 || (stickX == 0 && stickY == 0))
                        {
                            if (isMoving) Console.WriteLine($" -> 已復歸 (誤差: {distance:F1})");
                            break;
                        }

                        // 3. 執行移動
                        if (!isMoving)
                        {
                            Console.WriteLine($"[Pos] 偏移 {distance:F0} px, 修正中...");
                            isMoving = true;
                        }

                        _controllerService.SetLeftStick(stickX, stickY);
                    }

                    // 4. 控制修正頻率 (20 FPS)
                    await Task.Delay(50, token);
                }
            }
            finally
            {
                // 確保離開時放開搖桿
                _controllerService.SetLeftStick(0, 0);
            }
        }

        static void CreateDefaultProfile()
        {
            var defaultProfile = new ScriptConfig
            {
                LoopDelayMs = 0,
                Skills = new List<SkillStep>
                {
                    new SkillStep { Note = "Q", Buttons = new List<string> { "LB", "X" }, PressDurationMs = 100, CoolDownMs = 300, IsSequential = true },
                    new SkillStep { Note = "W", Buttons = new List<string> { "LB", "Y" }, PressDurationMs = 50, CoolDownMs = 300, IsSequential = true },
                    new SkillStep { Note = "E", Buttons = new List<string> { "LB", "B" }, PressDurationMs = 50, CoolDownMs = 300, IsSequential = true },
                    new SkillStep { Note = "R", Buttons = new List<string> { "LB", "A" }, PressDurationMs = 50, CoolDownMs = 300, IsSequential = true },
                    new SkillStep { Note = "F1喝水", Buttons = new List<string> { "RB", "X" }, PressDurationMs = 50, CoolDownMs = 300, IsSequential = true },
                    new SkillStep { Note = "A", Buttons = new List<string> { "LT", "X" }, PressDurationMs = 50, CoolDownMs = 300, IsSequential = true },
                    new SkillStep { Note = "S", Buttons = new List<string> { "LT", "Y" }, PressDurationMs = 50, CoolDownMs = 300, IsSequential = true },
                    new SkillStep { Note = "D", Buttons = new List<string> { "LT", "B" }, PressDurationMs = 50, CoolDownMs = 300, IsSequential = true },
                    new SkillStep { Note = "F", Buttons = new List<string> { "LT", "A" }, PressDurationMs = 50, CoolDownMs = 300, IsSequential = true }
                }
            };
            File.WriteAllText(CONFIG_PATH, JsonConvert.SerializeObject(defaultProfile, Formatting.Indented));
            Console.WriteLine($"已建立預設設定檔: {CONFIG_PATH}");
        }
    }
}