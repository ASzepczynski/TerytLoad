// Copyright (c) 2025-2026 Andrzej Szepczyñski. All rights reserved.

namespace TerytLoad.Pages.VerifyAddresses.Services
{
    public class AddressNormalizer
    {
        public string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            return text.Trim()
                .ToUpperInvariant()
                .Replace(" ", "")
                .Replace("-", "");
        }

        public string MarkIfDifferent(string source, string found)
        {
            var sourceNorm = Normalize(source);
            var foundNorm = Normalize(found);

            bool sourceEmpty = string.IsNullOrWhiteSpace(sourceNorm);
            bool foundEmpty = string.IsNullOrWhiteSpace(foundNorm);

            if (sourceEmpty && foundEmpty)
                return source;

            if (sourceEmpty != foundEmpty)
                return $"*{source}";

            if (sourceNorm != foundNorm)
                return $"*{source}";

            return source;
        }
    }
}