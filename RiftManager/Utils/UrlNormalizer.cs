using System;
using System.Text.RegularExpressions;

namespace RiftManager.Utils
{
    /// <summary>
    /// Single source of truth for normalizing Riot embed/navigation URLs.
    /// Why: NavigationParser stripped "{locale}" to "" leaving "//" (see log
    /// "wwpub-hall-of-legends-embed-2026//"), while EventProcessor replaced it
    /// with "en-us". Two places doing it differently caused double slashes and
    /// inconsistent fetch URLs. All URL cleaning must go through here.
    /// URL shapes observed:
    ///   nav iframed:      https://embed.rgpub.io/{slug}/{locale}/
    ///   detail metagame:  https://embed.rgpub.io/{slug}/{locale}/ (+ metagameId)
    ///   tft clientconfig: .../{bcplocale}/...  (same treatment)
    ///   cdn dist:         https://assetcdn.rgpub.io/public/live/bundle-offload/{uuid}/{hash}/...
    /// </summary>
    public static class UrlNormalizer
    {
        public const string DefaultLocale = "en-us";

        public static string NormalizeEmbedUrl(string url, string locale = DefaultLocale)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;

            string result = url.Trim();
            result = result.Replace("{locale}", locale ?? DefaultLocale, StringComparison.OrdinalIgnoreCase);
            result = result.Replace("{bcplocale}", locale ?? DefaultLocale, StringComparison.OrdinalIgnoreCase);

            // Collapse duplicate slashes in path, preserving "https://".
            result = Regex.Replace(result, @"(?<!:)/{2,}", "/");

            // Embed pages expect a trailing slash for relative resolution.
            if (result.StartsWith("https://embed.rgpub.io/", StringComparison.OrdinalIgnoreCase)
                && !result.EndsWith("/"))
            {
                result += "/";
            }

            return result;
        }

        public static bool IsEmbedUrl(string url)
        {
            return !string.IsNullOrEmpty(url)
                && url.Contains("embed.rgpub.io", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Ensures a CDN base URL ends with exactly one "/".
        /// </summary>
        public static string EnsureTrailingSlash(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            return url.TrimEnd('/') + "/";
        }
    }
}
