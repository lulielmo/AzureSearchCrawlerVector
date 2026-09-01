using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using System.Diagnostics;

namespace AzureSearchCrawler.Ocr
{
    /// <summary>
    /// Runs the local Tesseract CLI against an image stream.
    /// This class is a process adapter around an installed tesseract binary and is excluded from code coverage
    /// because exercising it requires a native Tesseract installation, which is unreliable in CI.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class TesseractOcrEngine : IOcrEngine
    {
        private readonly string _tesseractPath;
        private readonly string _language;
        private readonly string? _tessDataPath;
        private readonly TimeSpan _timeout;

        public TesseractOcrEngine(OcrOptions options, TimeSpan? timeout = null)
        {
            ArgumentNullException.ThrowIfNull(options);
            _tesseractPath = string.IsNullOrWhiteSpace(options.TesseractPath)
                ? "tesseract"
                : options.TesseractPath;
            _language = string.IsNullOrWhiteSpace(options.Language) ? "swe+eng" : options.Language;
            _tessDataPath = options.TessDataPath;
            _timeout = timeout ?? TimeSpan.FromSeconds(120);
        }

        public async Task<string> RecognizeAsync(Stream image, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(image);

            Stream seekable = image;
            MemoryStream? copy = null;
            if (!image.CanSeek)
            {
                copy = new MemoryStream();
                await image.CopyToAsync(copy, cancellationToken);
                copy.Position = 0;
                seekable = copy;
            }

            var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{DetectExtension(seekable)}");
            try
            {
                await using (var fileStream = File.Create(tempFile))
                {
                    seekable.Position = 0;
                    await seekable.CopyToAsync(fileStream, cancellationToken);
                }

                var arguments = $"\"{tempFile}\" stdout -l {_language}";
                if (!string.IsNullOrWhiteSpace(_tessDataPath))
                {
                    arguments += $" --tessdata-dir \"{_tessDataPath}\"";
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = _tesseractPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                if (!process.Start())
                {
                    throw new InvalidOperationException($"Failed to start Tesseract at '{_tesseractPath}'.");
                }

                var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_timeout);
                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException)
                {
                    TryKill(process);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    throw new TimeoutException(
                        $"Tesseract timed out after {_timeout.TotalSeconds:0} seconds for '{_tesseractPath}'.");
                }

                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"Tesseract exited with code {process.ExitCode}: {stderr.Trim()}");
                }

                return stdout.Trim();
            }
            finally
            {
                copy?.Dispose();
                TryDelete(tempFile);
            }
        }

        private static string DetectExtension(Stream stream)
        {
            Span<byte> header = stackalloc byte[12];
            var originalPosition = stream.Position;
            var read = stream.Read(header);
            stream.Position = originalPosition;

            if (read >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
            {
                return ".png";
            }

            if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8)
            {
                return ".jpg";
            }

            if (read >= 6 && header[0] == (byte)'G' && header[1] == (byte)'I' && header[2] == (byte)'F')
            {
                return ".gif";
            }

            if (read >= 12
                && header[0] == (byte)'R'
                && header[1] == (byte)'I'
                && header[2] == (byte)'F'
                && header[3] == (byte)'F'
                && header[8] == (byte)'W')
            {
                return ".webp";
            }

            return ".png";
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
                // Best-effort cleanup when Tesseract hangs.
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // Temp file cleanup should not fail OCR handling.
            }
        }
    }
}
