using AzureSearchCrawler.Models;

namespace AzureSearchCrawler.Ocr
{
    /// <summary>
    /// Decides whether a page should use OCR and which images look like real content rather than decoration.
    /// </summary>
    public class ThinContentDetector
    {
        private static readonly string[] DecorativeSrcPatterns =
        [
            "logo",
            "icon",
            "sprite",
            "pixel",
            "tracking",
            "1x1",
            "spacer",
            "badge"
        ];

        private const int MinContentDimension = 50;

        /// <summary>
        /// Returns true when the page has too little body text and at least one content image.
        /// </summary>
        public virtual bool ShouldUseOcr(
            string effectiveBodyText,
            IReadOnlyList<PageImage> contentImages,
            int textThreshold)
        {
            return effectiveBodyText.Length < textThreshold && contentImages.Count > 0;
        }

        /// <summary>
        /// Filters decorative images and caps how many images are OCR'd on one page.
        /// </summary>
        public virtual IReadOnlyList<PageImage> SelectContentImages(
            IReadOnlyList<PageImage> images,
            int maxImages)
        {
            if (images.Count == 0 || maxImages <= 0)
            {
                return Array.Empty<PageImage>();
            }

            return images
                .Where(IsContentImage)
                .Take(maxImages)
                .ToList();
        }

        public virtual bool IsContentImage(PageImage image)
        {
            if (string.IsNullOrWhiteSpace(image.Src) && image.AbsoluteUri == null)
            {
                return false;
            }

            var src = image.Src ?? string.Empty;
            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (image.AbsoluteUri != null
                && image.AbsoluteUri.Scheme != Uri.UriSchemeHttp
                && image.AbsoluteUri.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }

            if (image.Width is > 0 and < MinContentDimension)
            {
                return false;
            }

            if (image.Height is > 0 and < MinContentDimension)
            {
                return false;
            }

            var srcLower = src.ToLowerInvariant();
            foreach (var pattern in DecorativeSrcPatterns)
            {
                if (srcLower.Contains(pattern, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
