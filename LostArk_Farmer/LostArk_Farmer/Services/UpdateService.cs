using LostArkAutoPlayer.Models;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression; // 若是 .NET Framework 需引用 System.IO.Compression.FileSystem
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace LostArkAutoPlayer.Services
{
    public class UpdateService
    {
        private readonly string _currentVersion;
        private readonly string _githubUser;
        private readonly string _githubRepo;

        public UpdateService(string currentVersion, string user, string repo)
        {
            _currentVersion = currentVersion;
            _githubUser = user;
            _githubRepo = repo;
        }

        /// <summary>
        /// 檢查並執行更新
        /// </summary>
        /// <returns>回傳 true 代表正在更新或是需要停止主程式；回傳 false 代表無更新，可繼續執行。</returns>
        public async Task<bool> CheckForUpdatesAsync()
        {
            try
            {
                Console.WriteLine("正在檢查更新...");
                using (var httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LostArkAutoPlayer", _currentVersion));
                    httpClient.Timeout = TimeSpan.FromSeconds(10);

                    string url = $"https://api.github.com/repos/{_githubUser}/{_githubRepo}/releases/latest";
                    var response = await httpClient.GetAsync(url);

                    if (!response.IsSuccessStatusCode)
                    {
                        // 請求失敗 (例如網路不通)，視為無更新，讓程式繼續跑
                        return false;
                    }

                    var content = await response.Content.ReadAsStringAsync();
                    var release = JsonConvert.DeserializeObject<GithubRelease>(content);

                    if (ShouldUpdate(release.TagName))
                    {
                        // ★ 這裡會等待使用者的 Y/N 選擇，以及後續的下載過程
                        return await PromptUpdateAsync(release);
                    }
                    else
                    {
                        Console.WriteLine($"目前版本 v{_currentVersion} 已是最新。");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Update Error] {ex.Message}");
                // 發生錯誤時不中斷主程式
                return false;
            }
            finally
            {
                Console.WriteLine();
            }
        }

        private bool ShouldUpdate(string tagName)
        {
            string vRemote = tagName.TrimStart('v', 'V');
            // 簡單的版本號比對 logic
            return Version.TryParse(vRemote, out Version remoteVer) &&
                   Version.TryParse(_currentVersion, out Version currentVer) &&
                   remoteVer > currentVer;
        }

        private async Task<bool> PromptUpdateAsync(GithubRelease release)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n★ 發現新版本: {release.TagName}");
            Console.WriteLine($"內容: {release.Body}");
            Console.ResetColor();

            // 優先尋找 .zip，其次 .exe
            var asset = release.Assets.Find(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                     ?? release.Assets.Find(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            if (asset == null)
            {
                Console.WriteLine("錯誤：找不到可用的更新檔案 (.zip 或 .exe)。");
                return false;
            }

            Console.WriteLine("\n是否立即更新? (Y/N) [10秒後自動跳過]");

            // 等待輸入，若超時則回傳 false
            bool hasInput = await Task.Run(() => WaitForInput(10000));

            if (hasInput)
            {
                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.Y)
                {
                    // ★ 關鍵修正：這裡使用 await，主程式會停在這裡直到下載完成
                    await PerformUpdateAsync(asset.BrowserDownloadUrl, asset.Name, asset.Size);

                    // 下載並執行 Bat 後通常會關閉程式，回傳 true 告知主程式停止
                    return true;
                }
            }

            Console.WriteLine("\n跳過更新。");
            return false;
        }

        private async Task PerformUpdateAsync(string url, string fileName, long size)
        {
            Console.WriteLine($"\n準備下載: {fileName}");
            string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UpdateTemp");
            string downloadPath = Path.Combine(tempDir, fileName);

            try
            {
                // 1. 清理與建立暫存目錄
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                // 2. 下載檔案
                using (var client = new HttpClient())
                using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = File.Create(downloadPath))
                {
                    var buffer = new byte[8192];
                    long totalRead = 0;
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read);
                        totalRead += read;
                        DrawProgressBar(totalRead, size);
                    }
                }

                Console.WriteLine("\n下載完成，正在處理檔案...");

                // 3. 如果是 ZIP，解壓縮
                if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("解壓縮中...");
                    ZipFile.ExtractToDirectory(downloadPath, tempDir);
                    File.Delete(downloadPath); // 刪除 zip 本身，只留內容
                }

                // 4. 執行 Bat 更新 (這會關閉當前程式)
                ExecuteBatchUpdate(tempDir);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n更新過程發生錯誤: {ex.Message}");
                // 這裡可以選擇是否拋出異常或讓使用者手動處理
            }
        }

        private void ExecuteBatchUpdate(string sourceDir)
        {
            string currentExe = Process.GetCurrentProcess().MainModule.FileName;
            string batchPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update_script.bat");
            string workingDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');

            // 批次檔邏輯：等待 -> 複製(更新) -> 刪除暫存 -> 重啟 -> 自刪
            string script = $@"
@echo off
echo Waiting for application to close...
timeout /t 2 /nobreak > nul
echo Updating files...
xcopy ""{sourceDir}\*"" ""{workingDir}"" /E /H /Y /C
echo Cleaning up...
rmdir /s /q ""{sourceDir}""
echo Restarting application...
start """" ""{currentExe}""
del ""%~f0""
";
            File.WriteAllText(batchPath, script);

            Process.Start(new ProcessStartInfo
            {
                FileName = batchPath,
                UseShellExecute = true,
                CreateNoWindow = true, // 設為 false 可以看到 cmd 視窗 debug
                WindowStyle = ProcessWindowStyle.Hidden
            });

            Console.WriteLine("更新腳本已啟動，程式即將關閉...");
            Environment.Exit(0);
        }

        private void DrawProgressBar(long current, long total)
        {
            if (total <= 0) return;
            int width = 40;
            double pct = (double)current / total;
            int filled = (int)(pct * width);
            Console.Write($"\r[{new string('#', filled)}{new string('-', width - filled)}] {pct:P0}");
        }

        private bool WaitForInput(int timeoutMs)
        {
            int elapsed = 0;
            while (elapsed < timeoutMs)
            {
                if (Console.KeyAvailable) return true;
                Thread.Sleep(100);
                elapsed += 100;
            }
            return false;
        }
    }
}