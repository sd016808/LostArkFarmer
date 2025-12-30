using LostArkAutoPlayer.Models;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression; // 需要引用 System.IO.Compression.FileSystem (如果是 .NET Framework)
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

        public async Task CheckForUpdatesAsync()
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

                    if (!response.IsSuccessStatusCode) return;

                    var content = await response.Content.ReadAsStringAsync();
                    var release = JsonConvert.DeserializeObject<GithubRelease>(content);

                    if (ShouldUpdate(release.TagName))
                    {
                        PromptUpdate(release);
                    }
                    else
                    {
                        Console.WriteLine($"目前版本 v{_currentVersion} 已是最新。");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Update Error] {ex.Message}");
            }
            Console.WriteLine();
        }

        private bool ShouldUpdate(string tagName)
        {
            string vRemote = tagName.TrimStart('v', 'V');
            return Version.TryParse(vRemote, out Version remoteVer) &&
                   Version.TryParse(_currentVersion, out Version currentVer) &&
                   remoteVer > currentVer;
        }

        private void PromptUpdate(GithubRelease release)
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
                return;
            }

            Console.WriteLine("\n是否立即更新? (Y/N) [10秒後自動跳過]");
            if (WaitForInput(5000))
            {
                if (Console.ReadKey(true).Key == ConsoleKey.Y)
                {
                    _ = PerformUpdateAsync(asset.BrowserDownloadUrl, asset.Name, asset.Size);
                }
            }
        }

        private async Task PerformUpdateAsync(string url, string fileName, long size)
        {
            Console.WriteLine($"\n準備下載: {fileName}");
            string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UpdateTemp");
            string downloadPath = Path.Combine(tempDir, fileName);

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

            // 4. 執行 Bat 更新
            ExecuteBatchUpdate(tempDir);
        }

        private void ExecuteBatchUpdate(string sourceDir)
        {
            string currentExe = Process.GetCurrentProcess().MainModule.FileName;
            string batchPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update_script.bat");
            string workingDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');

            // 專業的 Bat 更新腳本：
            // 1. 等待主程式關閉
            // 2. 使用 xcopy 把 temp 資料夾內容全部覆蓋到根目錄 (/E包含子目錄, /Y強制覆蓋, /H包含隱藏檔)
            // 3. 刪除 temp 資料夾
            // 4. 重啟主程式
            // 5. 刪除 bat
            string script = $@"
@echo off
timeout /t 2 /nobreak > nul
xcopy ""{sourceDir}\*"" ""{workingDir}"" /E /H /Y /C
rmdir /s /q ""{sourceDir}""
start """" ""{currentExe}""
del ""%~f0""
";
            File.WriteAllText(batchPath, script);

            Process.Start(new ProcessStartInfo
            {
                FileName = batchPath,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

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