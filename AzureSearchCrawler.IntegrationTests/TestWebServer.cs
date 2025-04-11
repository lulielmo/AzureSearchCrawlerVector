using System.Diagnostics;
using System.Net.Http;
using System.Reflection;

namespace AzureSearchCrawler.IntegrationTests
{
    public class TestWebServer : IAsyncLifetime
    {
        private Process? _webServerProcess;
        private readonly string _websitePath;
        private readonly int _port;
        private readonly string _websiteName;
        private readonly HttpClient _httpClient;
        private const int MaxStartupAttempts = 10;
        private const int StartupRetryDelayMs = 1000;

        public string BaseUrl => $"http://localhost:{_port}";

        public TestWebServer(string websiteName, int port)
        {
            _websiteName = websiteName;
            _port = port;

            // Find the path to the IntegrationTests folder based on the assembly location
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var assemblyDirectory = Path.GetDirectoryName(assemblyLocation)!;
            var projectRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..\\..\\..\\.."));
            _websitePath = Path.Combine(projectRoot, "IntegrationTests", _websiteName);

            _httpClient = new HttpClient();
        }

        public async Task InitializeAsync()
        {
            if (!Directory.Exists(_websitePath))
            {
                throw new DirectoryNotFoundException($"Test website directory not found: {_websitePath}");
            }

            Console.WriteLine($"Starting test website {_websiteName} at {_websitePath} on port {_port}");

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --urls=http://localhost:{_port}",
                WorkingDirectory = _websitePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _webServerProcess = new Process { StartInfo = startInfo };
            _webServerProcess.OutputDataReceived += (sender, e) => 
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Console.WriteLine($"[{_websiteName}] {e.Data}");
            };
            _webServerProcess.ErrorDataReceived += (sender, e) => 
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Console.Error.WriteLine($"[{_websiteName}] ERROR: {e.Data}");
            };

            Console.WriteLine($"Starting process: {startInfo.FileName} {startInfo.Arguments}");
            _webServerProcess.Start();
            _webServerProcess.BeginOutputReadLine();
            _webServerProcess.BeginErrorReadLine();

            // Wait for the server to start and respond
            for (int attempt = 0; attempt < MaxStartupAttempts; attempt++)
            {
                try
                {
                    Console.WriteLine($"Attempt {attempt + 1}/{MaxStartupAttempts} to connect to {BaseUrl}");
                    var response = await _httpClient.GetAsync(BaseUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Successfully connected to {BaseUrl}");
                        return;
                    }
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"Connection attempt {attempt + 1} failed: {ex.Message}");
                    // Server not ready yet, continue waiting
                }

                await Task.Delay(StartupRetryDelayMs);
            }

            if (_webServerProcess.HasExited)
            {
                throw new Exception($"Failed to start test website {_websiteName}. Exit code: {_webServerProcess.ExitCode}");
            }
            else
            {
                throw new Exception($"Test website {_websiteName} failed to respond after {MaxStartupAttempts} attempts");
            }
        }

        public async Task DisposeAsync()
        {
            if (_webServerProcess != null)
            {
                try
                {
                    if (!_webServerProcess.HasExited)
                    {
                        _webServerProcess.Kill();
                        await _webServerProcess.WaitForExitAsync();
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error shutting down test website {_websiteName}: {ex.Message}");
                }
                finally
                {
                    _webServerProcess.Dispose();
                }
            }

            // Try to find and terminate any remaining processes
            try
            {
                var processes = Process.GetProcessesByName("TestWebsite");
                foreach (var process in processes)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                            await process.WaitForExitAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Error killing leftover process {process.Id}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error finding leftover processes: {ex.Message}");
            }

            _httpClient.Dispose();
        }
    }
} 