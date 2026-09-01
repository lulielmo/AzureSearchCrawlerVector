using System.Security.Cryptography;
using System.Text;

namespace AzureSearchCrawler.Utils
{
    /// <summary>
    /// Utility class for hash operations.
    /// </summary>
    public static class HashUtils
    {
        /// <summary>
        /// Creates a SHA512 hash of the input string.
        /// </summary>
        /// <param name="strData">The string to hash.</param>
        /// <returns>A hexadecimal string representation of the SHA512 hash.</returns>
        public static string CreateSHA512(string strData)
        {
            var message = Encoding.UTF8.GetBytes(strData);
            var hashValue = SHA512.HashData(message);
            return hashValue.Aggregate("", (current, x) => current + $"{x:x2}");
        }
    }
} 