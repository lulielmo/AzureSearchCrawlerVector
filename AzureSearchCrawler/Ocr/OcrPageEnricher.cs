using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;

namespace AzureSearchCrawler.Ocr
{
    /// <summary>
    /// Appends Tesseract OCR text to extracted page content when the page is image-heavy and text-poor.
    /// </summary>
    public class OcrPageEnricher
    {
        private readonly IOcrEngine _ocrEngine;
        private readonly IImageDownloader _imageDownloader;
        private readonly ThinContentDetector _detector;
        private readonly IConsole _console;
        private readonly OcrOptions _options;

        public OcrPageEnricher(
            IOcrEngine ocrEngine,
            IImageDownloader imageDownloader,
            IConsole console,
            OcrOptions options,
            ThinContentDetector? detector = null)
        {
            _ocrEngine = ocrEngine ?? throw new ArgumentNullException(nameof(ocrEngine));
            _imageDownloader = imageDownloader ?? throw new ArgumentNullException(nameof(imageDownloader));
            _console = console ?? throw new ArgumentNullException(nameof(console));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _detector = detector ?? new ThinContentDetector();
        }

        /// <summary>
        /// Returns extracted text, optionally concatenated with OCR text from content images.
        /// A failed OCR on one image is logged and skipped; the page is still indexed.
        /// </summary>
        public async Task<string> EnrichAsync(
            ExtractedPageContent extracted,
            Uri pageUri,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(extracted);
            ArgumentNullException.ThrowIfNull(pageUri);

            var contentImages = _detector.SelectContentImages(extracted.Images, _options.MaxImagesPerPage);
            if (!_detector.ShouldUseOcr(extracted.EffectiveBodyText, contentImages, _options.TextThreshold))
            {
                return extracted.Text;
            }

            _console.WriteLine(
                $"Page {pageUri} has little body text ({extracted.EffectiveBodyText.Length} chars) " +
                $"and {contentImages.Count} content image(s); running OCR",
                LogLevel.Information);

            var ocrParts = new List<string>();
            foreach (var image in contentImages)
            {
                if (image.AbsoluteUri == null)
                {
                    _console.WriteLine(
                        $"Skipping image with unresolved src '{image.Src}' on {pageUri}",
                        LogLevel.Debug);
                    continue;
                }

                try
                {
                    await using var stream = await _imageDownloader.DownloadAsync(image.AbsoluteUri, cancellationToken);
                    if (stream == null)
                    {
                        _console.WriteLine(
                            $"Could not download image {image.AbsoluteUri} for OCR",
                            LogLevel.Warning);
                        continue;
                    }

                    var ocrText = await _ocrEngine.RecognizeAsync(stream, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(ocrText))
                    {
                        ocrParts.Add(ocrText.Trim());
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _console.WriteLine(
                        $"OCR failed for image {image.AbsoluteUri}: {ex.Message}",
                        LogLevel.Warning);
                    _console.WriteLine($"Technical details: {ex}", LogLevel.Debug);
                }
            }

            var combinedOcr = string.Join(" ", ocrParts);
            _console.WriteLine(
                $"OCR extracted {combinedOcr.Length} characters from {ocrParts.Count} image(s) on {pageUri}",
                LogLevel.Information);

            if (string.IsNullOrWhiteSpace(combinedOcr))
            {
                return extracted.Text;
            }

            if (string.IsNullOrWhiteSpace(extracted.Text))
            {
                return combinedOcr;
            }

            return $"{extracted.Text} {combinedOcr}";
        }
    }
}
