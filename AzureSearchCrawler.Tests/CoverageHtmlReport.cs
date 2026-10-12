namespace AzureSearchCrawler.Testing
{
    using System.Diagnostics;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Generates the HTML coverage report when the test host exits.
    /// <c>dotnet test</c> on the .NET 10 SDK runs the test executable directly, so the
    /// MSBuild report target is not part of that command. The cobertura file is written
    /// by the coverage extension before the process exits.
    /// </summary>
    internal static class CoverageHtmlReport
    {
        private static int _started;

        [ModuleInitializer]
        internal static void Register()
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Generate();
        }

        private static void Generate()
        {
            if (Interlocked.Exchange(ref _started, 1) != 0)
            {
                return;
            }

            try
            {
                var settingsPath = Path.Combine(AppContext.BaseDirectory, "coverage-report.settings");
                if (!File.Exists(settingsPath))
                {
                    return;
                }

                var parts = File.ReadAllText(settingsPath).Split('|', 2);
                if (parts.Length != 2)
                {
                    return;
                }

                var projectDirectory = parts[0].Trim();
                var reportGenerator = parts[1].Trim();
                var coberturaPath = Path.Combine(projectDirectory, "TestResults", "coverage.cobertura.xml");
                var htmlDirectory = Path.Combine(projectDirectory, "TestResults", "CoverageReport");
                if (!File.Exists(reportGenerator) || !WaitForCoverageFile(coberturaPath))
                {
                    return;
                }

                Directory.CreateDirectory(htmlDirectory);
                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    WorkingDirectory = projectDirectory,
                    UseShellExecute = false
                };
                startInfo.ArgumentList.Add(reportGenerator);
                startInfo.ArgumentList.Add($"-reports:{coberturaPath}");
                startInfo.ArgumentList.Add($"-targetdir:{htmlDirectory}");
                startInfo.ArgumentList.Add("-reporttypes:Html");

                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return;
                }

                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                Task.WaitAll(output, error);
                if (process.ExitCode != 0)
                {
                    Console.Error.WriteLine(error.Result);
                    Console.Error.WriteLine($"ReportGenerator exited with code {process.ExitCode}.");
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"HTML coverage report was not generated: {exception.Message}");
            }
        }

        private static bool WaitForCoverageFile(string coberturaPath)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                if (File.Exists(coberturaPath))
                {
                    return true;
                }

                Thread.Sleep(100);
            }

            return File.Exists(coberturaPath);
        }
    }
}
