using System.Text;

namespace ParentalGuard.Service;

public sealed class HostsFileBlocker
{
    private const string StartMarker = "# ParentalGuard blocked sites start";
    private const string EndMarker = "# ParentalGuard blocked sites end";
    private readonly string _hostsPath;

    public HostsFileBlocker(string? hostsPath = null)
    {
        _hostsPath = hostsPath ?? Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");
    }

    public void SyncBlockedSites(IEnumerable<string> blockedDomains)
    {
        var normalizedDomains = blockedDomains
            .SelectMany(ExpandDomainVariants)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingContent = File.Exists(_hostsPath) ? File.ReadAllText(_hostsPath) : string.Empty;
        var cleanedContent = RemoveManagedSection(existingContent).TrimEnd();

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(cleanedContent))
        {
            builder.AppendLine(cleanedContent);
            builder.AppendLine();
        }

        builder.AppendLine(StartMarker);
        foreach (var domain in normalizedDomains)
        {
            builder.Append("127.0.0.1 ");
            builder.AppendLine(domain);
        }
        builder.AppendLine(EndMarker);

        var newContent = builder.ToString();
        if (!string.Equals(existingContent, newContent, StringComparison.Ordinal))
        {
            File.WriteAllText(_hostsPath, newContent);
        }
    }

    private static IEnumerable<string> ExpandDomainVariants(string domain)
    {
        var normalized = NormalizeDomain(domain);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            yield break;
        }

        yield return normalized;

        if (!normalized.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"www.{normalized}";
        }
    }

    private static string NormalizeDomain(string domain)
    {
        var normalized = domain.Trim().ToLowerInvariant();
        return normalized.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? normalized[4..] : normalized;
    }

    private static string RemoveManagedSection(string content)
    {
        var startIndex = content.IndexOf(StartMarker, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            return content;
        }

        var endIndex = content.IndexOf(EndMarker, startIndex, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return content[..startIndex];
        }

        var endMarkerLineEnd = content.IndexOf('\n', endIndex);
        var suffix = endMarkerLineEnd >= 0 ? content[(endMarkerLineEnd + 1)..] : string.Empty;
        return content[..startIndex] + suffix;
    }
}
