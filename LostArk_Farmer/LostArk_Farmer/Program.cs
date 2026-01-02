using LostArkAutoPlayer.Models;
using LostArkAutoPlayer.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing; // 需引用 System.Drawing
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LostArkAutoPlayer
{
    class Program
    {
        const string VERSION = "1.0.3"; // 版本號更新
        const string CONFIG_PATH = "skill_config.json";

        static volatile bool _isRunning = false;
        static CancellationTokenSource _cts;

        // 服務宣告
        static VirtualControllerService _controllerService;
        static GameWindowService _windowService;
        static VisualPositionService _visualPosService;

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.Title = $"LostArkFarmer v{VERSION}";

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"=== Lost Ark Farmer v{VERSION} ===");
            Console.WriteLine("模式: 後台截圖 (PrintWindow)");
            Console.ResetColor();

            // 1. 服務實例化
            // 這裡省略 UpdateService 以簡化程式碼，你可以自己加回去
            _windowService = new GameWindowService();
            _controllerService = new VirtualControllerService();
            _visualPosService = new VisualPositionService();
            var inputListener = new InputListener();

            // 2. 讀取或建立設定檔
            if (!File.Exists(CONFIG_PATH))
            {
                CreateDefaultProfile();
                Console.WriteLine("請編輯設定檔後重新啟動程式。");
                Console.ReadKey();
                return;
            }

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

            try
            {
                // 3. 初始化
                _controllerService.Initialize();
                await _windowService.WaitForGameProcessAsync();

                // 4. 事件綁定
                inputListener.OnStartRequested += (s, e) =>
                {
                    if (!_isRunning)
                    {
                        _isRunning = true;
                        // 啟動時設定原點
                        if (config.EnableAutoReturn)
                        {
                            CaptureAndSetOrigin();
                        }
                    }
                };

                inputListener.OnStopRequested += (s, e) =>
                {
                    if (_isRunning)
                    {
                        Console.WriteLine("\n[Cmd] 停止指令...");
                        _isRunning = false;
                        _cts?.Cancel();
                    }
                };

                inputListener.StartListening();

                Console.WriteLine($"\n[Ready] 自動回原點功能: {(config.EnableAutoReturn ? "開啟" : "關閉")}");
                Console.WriteLine("[Hotkeys] F9: Start | F10: Stop | Ctrl+C: Exit");

                // 5. 主程式迴圈
                while (true)
                {
                    // 等待啟動
                    while (!_isRunning) await Task.Delay(100);

                    Console.WriteLine("\n>>> 腳本啟動 <<<");
                    _cts = new CancellationTokenSource();
                    var token = _cts.Token;

                    try
                    {
                        while (!token.IsCancellationRequested && _isRunning)
                        {
                            // === A. 執行技能序列 ===
                            foreach (var step in config.Skills)
                            {
                                token.ThrowIfCancellationRequested();
                                Console.Write($"[Skill] {step.Note} ... ");
                                _windowService.KeepAwake(); // 防止進入休眠

                                // 按下按鍵
                                if (step.Buttons != null)
                                {
                                    foreach (var btn in step.Buttons)
                                    {
                                        _controllerService.SendInput(btn, true);
                                        if (step.IsSequential && step.Buttons.Count > 1) await Task.Delay(200, token);
                                    }
                                }
                                await Task.Delay(step.PressDurationMs, token);

                                // 放開按鍵
                                if (step.Buttons != null)
                                {
                                    for (int i = step.Buttons.Count - 1; i >= 0; i--)
                                        _controllerService.SendInput(step.Buttons[i], false);
                                }

                                Console.WriteLine("OK");
                                if (step.CoolDownMs > 0) await Task.Delay(step.CoolDownMs, token);
                            }

                            // === B. 自動回正邏輯 ===
                            if (config.EnableAutoReturn)
                            {
                                await ExecuteReturnToOriginAsync(token);
                            }

                            // === C. 迴圈延遲 ===
                            if (config.LoopDelayMs > 0) await Task.Delay(config.LoopDelayMs, token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine(">>> 腳本已暫停 <<<");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Error] 執行期間發生錯誤: {ex.Message}");
                        _isRunning = false;
                    }
                    finally
                    {
                        // 清理狀態
                        _controllerService.ResetAllInputs();
                        _visualPosService.ResetTarget();

                        if (_cts != null) { _cts.Dispose(); _cts = null; }
                        Console.WriteLine("[Ready] 等待 F9 重新啟動...");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Fatal] 主程式崩潰: {ex.Message}");
                Console.ReadKey();
            }
            finally
            {
                _controllerService.Dispose();
                _visualPosService.Dispose();
            }
        }

        // --- 輔助方法 ---

        /// <summary>
        /// 截圖並設定原點 (使用 CaptureWindow)
        /// </summary>
        static void CaptureAndSetOrigin()
        {
            try
            {
                // 取得遊戲視窗 Handle
                IntPtr handle = _windowService.GameHandle;
                if (handle == IntPtr.Zero) return;

                // 使用後台截圖
                using (var bmp = ScreenCapture.CaptureWindow(handle))
                {
                    if (bmp != null)
                    {
                        if (_visualPosService.SetCurrentAsTarget(bmp))
                        {
                            Console.WriteLine("[Pos] 原點已鎖定。");
                        }
                        else
                        {
                            Console.WriteLine("[Pos Warning] 鎖定失敗 (沒抓到箭頭)");
                        }
                    }
                    else
                    {
                        Console.WriteLine("[Error] 截圖失敗 (可能是全螢幕獨佔模式導致黑屏，請改視窗模式)");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Pos Error] 設定原點失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 執行回正動作
        /// </summary>
        static async Task ExecuteReturnToOriginAsync(CancellationToken token)
        {
            if (!_visualPosService.IsTargetSet) return;

            // 設定超時 (例如 3 秒)，避免卡死
            var timeoutLimit = DateTime.Now.AddSeconds(3);
            bool isMoving = false;

            try
            {
                while (DateTime.Now < timeoutLimit && !token.IsCancellationRequested)
                {
                    IntPtr handle = _windowService.GameHandle;
                    if (handle == IntPtr.Zero) break;

                    // 使用後台截圖
                    using (var bmp = ScreenCapture.CaptureWindow(handle))
                    {
                        // 若截圖失敗 (黑屏)，中斷回正，避免誤判
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
                            // 第一次移動時顯示訊息
                            Console.WriteLine($"[Pos] 偏移 {distance:F0} px, 修正中...");
                            isMoving = true;
                        }

                        _controllerService.SetLeftStick(stickX, stickY);
                    }

                    // 4. 等待一下 (控制修正頻率)
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
                EnableAutoReturn = true,
                Skills = new List<SkillStep>
                {
                    // Q 技能 (LB + X)
                    new SkillStep { Note = "Q", Buttons = new List<string> { "LB", "X" }, PressDurationMs = 100, CoolDownMs = 300, IsSequential = true },
                    
                    // W 技能 (LB + Y)
                    new SkillStep { Note = "W", Buttons = new List<string> { "LB", "Y" }, PressDurationMs = 50, CoolDownMs = 300, IsSequential = true },
                    
                    // E 技能 (LB + B)
                    new SkillStep { Note = "E", Buttons = new List<string> { "LB", "B" }, PressDurationMs = 50, CoolDownMs = 300, IsSequential = true },
                    
                    // R 技能 (LB + A)
                    new SkillStep { Note = "R", Buttons = new List<string> { "LB", "A" }, PressDurationMs = 50, CoolDownMs = 300, IsSequential = true },
                    
                    // F1 喝水 (RB + X)
                    new SkillStep { Note = "F1喝水", Buttons = new List<string> { "RB", "X" }, PressDurationMs = 50, CoolDownMs = 300, IsSequential = true },
                    
                    // A 技能 (LT + X)
                    new SkillStep { Note = "A", Buttons = new List<string> { "LT", "X" }, PressDurationMs = 50, CoolDownMs = 300, IsSequential = true },
                    
                    // S 技能 (LT + Y)
                    new SkillStep { Note = "S", Buttons = new List<string> { "LT", "Y" }, PressDurationMs = 50, CoolDownMs = 300, IsSequential = true },
                    
                    // D 技能 (LT + B)
                    new SkillStep { Note = "D", Buttons = new List<string> { "LT", "B" }, PressDurationMs = 50, CoolDownMs = 300, IsSequential = true },
                    
                    // F 技能 (LT + A)
                    new SkillStep { Note = "F", Buttons = new List<string> { "LT", "A" }, PressDurationMs = 50, CoolDownMs = 300, IsSequential = true }
                }
            };
            File.WriteAllText(CONFIG_PATH, JsonConvert.SerializeObject(defaultProfile, Formatting.Indented));
            Console.WriteLine($"已建立預設設定檔: {CONFIG_PATH}");
        }
    }
}