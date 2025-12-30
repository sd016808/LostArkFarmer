using LostArkAutoPlayer.Models;
using LostArkAutoPlayer.Services;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LostArkAutoPlayer
{
    class Program
    {
        const string VERSION = "1.0.1";
        const string CONFIG_PATH = "script_profile.json";

        static volatile bool _isRunning = false;
        static CancellationTokenSource _cts;
        static VirtualControllerService _controllerService;

        static async Task Main(string[] args)
        {
            // 讓 Console 支援 Emoji 顯示
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.Title = $"LostArkFarmer v{VERSION}";

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"=== Lost Ark Farmer v{VERSION} ===");
            Console.ResetColor();

            var updateService = new UpdateService(VERSION, "sd016808", "LostArkFarmer");
            var windowService = new GameWindowService();
            var inputListener = new InputListener();
            _controllerService = new VirtualControllerService();

            // 1. 檢查更新
            await updateService.CheckForUpdatesAsync();

            // 2. 載入設定
            if (!File.Exists(CONFIG_PATH))
            {
                CreateDefaultProfile();
                Console.ReadKey();
                return;
            }

            ScriptConfig config;
            try
            {
                var jsonContent = File.ReadAllText(CONFIG_PATH);
                config = JsonConvert.DeserializeObject<ScriptConfig>(jsonContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config Error] 設定檔讀取失敗: {ex.Message}");
                Console.ReadKey();
                return;
            }

            if (config == null || config.Skills == null)
            {
                Console.WriteLine("[Config Error] 設定檔格式錯誤。");
                Console.ReadKey();
                return;
            }

            try
            {
                // 3. 初始化硬體與視窗
                _controllerService.Initialize();
                await windowService.WaitForGameProcessAsync();

                inputListener.OnStartRequested += (s, e) =>
                {
                    if (!_isRunning)
                    {
                        _isRunning = true;
                    }
                };

                inputListener.OnStopRequested += (s, e) =>
                {
                    if (_isRunning)
                    {
                        Console.WriteLine("\n[Cmd] 收到暫停指令...");
                        _isRunning = false;
                        _cts?.Cancel();
                    }
                };

                inputListener.StartListening();

                Console.WriteLine($"\n[Ready] 腳本已載入 ({config.Skills.Count} 步驟)");
                Console.WriteLine("[Hotkeys] F9: Start | F10: Stop | Ctrl+C: Exit");

                // 4. 主迴圈 (State Machine)
                while (true)
                {
                    // 階段一：等待啟動
                    while (!_isRunning)
                    {
                        await Task.Delay(100);
                    }

                    // 階段二：開始執行
                    Console.WriteLine("\n>>> 腳本啟動 (Script Started) <<<");
                    _cts = new CancellationTokenSource();

                    try
                    {
                        var token = _cts.Token;
                        while (!token.IsCancellationRequested && _isRunning)
                        {
                            foreach (var step in config.Skills)
                            {
                                token.ThrowIfCancellationRequested();

                                Console.Write($"[Exec] {step.Note} ... ");
                                windowService.KeepAwake();
                                await Task.Delay(20, token);

                                // 按下 (Press)
                                if (step.Buttons != null)
                                {
                                    foreach (var btn in step.Buttons)
                                    {
                                        _controllerService.SendInput(btn, true);
                                        // 依序按下邏輯
                                        if (step.IsSequential && step.Buttons.Count > 1)
                                            await Task.Delay(200, token);
                                    }
                                }

                                // 持續按住 (Hold)
                                await Task.Delay(step.PressDurationMs, token);

                                // 放開 (Release)
                                if (step.Buttons != null)
                                {
                                    for (int i = step.Buttons.Count - 1; i >= 0; i--)
                                    {
                                        _controllerService.SendInput(step.Buttons[i], false);
                                    }
                                }

                                Console.WriteLine("OK");
                                if (step.CoolDownMs > 0) await Task.Delay(step.CoolDownMs, token);
                            }

                            // 迴圈延遲
                            if (config.LoopDelayMs > 0) await Task.Delay(config.LoopDelayMs, token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine(">>> 腳本已暫停 (Stopped) <<<");
                    }
                    catch (ObjectDisposedException)
                    {
                        Console.WriteLine(">>> 核心重置中 (Resetting) <<<");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Error] 發生未預期錯誤: {ex.Message}");
                    }
                    finally
                    {
                        _controllerService.ResetAllInputs();
                        _isRunning = false;

                        if (_cts != null)
                        {
                            _cts.Dispose();
                            _cts = null;
                        }

                        Console.WriteLine("[Ready] 等待 F9 啟動...");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Fatal Error] 主程式崩潰: {ex.Message}");
                Console.ReadKey();
            }
            finally
            {
                _controllerService.Dispose();
            }
        }

        static void CreateDefaultProfile()
        {
            var defaultProfile = new ScriptConfig
            {
                LoopDelayMs = 0,
                Skills = new System.Collections.Generic.List<SkillStep>
                {
                    new SkillStep { Note = "範例-移動", Buttons = new System.Collections.Generic.List<string> { "LEFT", "UP" }, PressDurationMs = 500, CoolDownMs = 100, IsSequential = false },
                    new SkillStep { Note = "範例-技能", Buttons = new System.Collections.Generic.List<string> { "LB", "X" }, PressDurationMs = 100, CoolDownMs = 100, IsSequential = true }
                }
            };
            File.WriteAllText(CONFIG_PATH, JsonConvert.SerializeObject(defaultProfile, Formatting.Indented));
            Console.WriteLine($"已建立預設設定檔: {CONFIG_PATH}");
        }
    }
}