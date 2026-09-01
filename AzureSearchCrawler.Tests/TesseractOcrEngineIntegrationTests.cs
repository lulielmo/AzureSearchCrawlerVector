using AzureSearchCrawler.Models;
using AzureSearchCrawler.Ocr;
using System.Diagnostics;
using Xunit;

namespace AzureSearchCrawler.Tests
{
    [Trait("Category", "Integration")]
    public class TesseractOcrEngineIntegrationTests
    {
        // 1x1 white PNG
        private static readonly byte[] TinyPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        [TesseractAvailableFact]
        public async Task RecognizeAsync_WithTinyPng_ReturnsString()
        {
            var engine = new TesseractOcrEngine(new OcrOptions
            {
                Enabled = true,
                Language = "eng"
            });

            await using var stream = new MemoryStream(TinyPng);
            var text = await engine.RecognizeAsync(stream);

            Assert.NotNull(text);
        }
    }

    public sealed class TesseractAvailableFactAttribute : FactAttribute
    {
        public TesseractAvailableFactAttribute()
        {
            if (!IsTesseractAvailable())
            {
                Skip = "Tesseract is not installed on PATH";
            }
        }

        private static bool IsTesseractAvailable()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "tesseract",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return false;
                }

                if (!process.WaitForExit(5000))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (Exception)
                    {
                        // Ignore cleanup failures while probing for Tesseract.
                    }

                    return false;
                }

                return process.ExitCode == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}

