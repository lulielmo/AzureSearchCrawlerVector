using System.Diagnostics;
using System.Net.Http;
using System.Reflection;

namespace AzureSearchCrawler.IntegrationTests
{
    public class TestSpaWebsiteFixture : IAsyncLifetime
    {
        private Process? _webServerProcess;
        private readonly string _websitePath;
        private readonly int _port;
        private readonly HttpClient _httpClient;
        private const int MaxStartupAttempts = 10;
        private const int StartupRetryDelayMs = 1000;

        public string BaseUrl => $"http://localhost:{_port}";

        public TestSpaWebsiteFixture()
        {
            _port = 3000;

            // Hitta sökvägen till IntegrationTests-mappen baserat på assembly-platsen
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var assemblyDirectory = Path.GetDirectoryName(assemblyLocation)!;
            var projectRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..\\..\\..\\.."));
            _websitePath = Path.Combine(projectRoot, "IntegrationTests", "test-spa-website");

            _httpClient = new HttpClient();
        }

        public async Task InitializeAsync()
        {
            if (!Directory.Exists(_websitePath))
            {
                throw new DirectoryNotFoundException($"Test website directory not found: {_websitePath}");
            }

            Console.WriteLine($"Starting test SPA website at {_websitePath} on port {_port}");

            var startInfo = new ProcessStartInfo
            {
                FileName = "npm.cmd",
                Arguments = "run dev",
                WorkingDirectory = _websitePath,
                UseShellExecute = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = true
            };

            _webServerProcess = new Process { StartInfo = startInfo };
            Console.WriteLine($"Starting process: {startInfo.FileName} {startInfo.Arguments}");
            _webServerProcess.Start();

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
                        await Task.Delay(500); // Extra buffer to ensure server is fully ready
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
                throw new Exception($"Failed to start test SPA website. Exit code: {_webServerProcess.ExitCode}");
            }
            else
            {
                throw new Exception($"Test SPA website failed to respond after {MaxStartupAttempts} attempts");
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
                        // Använd taskkill för att avsluta hela process-trädet
                        var killProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "taskkill",
                                Arguments = $"/F /T /PID {_webServerProcess.Id}",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            }
                        };
                        
                        killProcess.Start();
                        await killProcess.WaitForExitAsync();
                        await _webServerProcess.WaitForExitAsync();
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error shutting down test SPA website: {ex.Message}");
                }
                finally
                {
                    _webServerProcess.Dispose();
                }
            }

            _httpClient.Dispose();
        }
    }
} 