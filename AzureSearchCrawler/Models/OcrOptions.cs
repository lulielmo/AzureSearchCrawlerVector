namespace AzureSearchCrawler.Models
{
    /// <summary>
    /// Configuration for optional local Tesseract OCR fallback on image-heavy pages.
    /// </summary>
    public sealed class OcrOptions
    {
        public bool Enabled { get; init; }

        /// <summary>
        /// Tesseract language codes, for example "swe+eng".
        /// </summary>
        public string Language { get; init; } = "swe+eng";

        /// <summary>
        /// Path to the tesseract executable, or "tesseract" when it is on PATH.
        /// </summary>
        public string TesseractPath { get; init; } = "tesseract";

        /// <summary>
        /// Optional path to a tessdata directory, passed as --tessdata-dir.
        /// </summary>
        public string? TessDataPath { get; init; }

        /// <summary>
        /// Run OCR when effective body text is shorter than this many characters.
        /// </summary>
        public int TextThreshold { get; init; } = 200;

        /// <summary>
        /// Maximum number of content images to OCR on a single page.
        /// </summary>
        public int MaxImagesPerPage { get; init; } = 10;

        public static OcrOptions Disabled { get; } = new();
    }
}
