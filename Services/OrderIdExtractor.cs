using System.Text.RegularExpressions;

namespace CasoDCodeConsumer.Services;

public sealed partial class OrderIdExtractor
{
    [GeneratedRegex(@"ORD[- ]?\d{3,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PreferredOrderRegex();

    [GeneratedRegex(@"\b\d{4,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex FallbackOrderRegex();

    public string? Extract(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        var preferredMatch = PreferredOrderRegex().Match(prompt);
        if (preferredMatch.Success)
        {
            var digits = Regex.Replace(preferredMatch.Value, @"\D", string.Empty);
            return $"ORD-{digits}";
        }

        var fallbackMatch = FallbackOrderRegex().Match(prompt);
        return fallbackMatch.Success ? fallbackMatch.Value : null;
    }
}
