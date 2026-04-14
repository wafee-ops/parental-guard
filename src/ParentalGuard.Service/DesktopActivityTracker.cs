using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ParentalGuard.Service;

public sealed class DesktopActivityTracker
{
    private static readonly HashSet<string> BrowserProcesses =
    [
        "chrome",
        "msedge",
        "firefox",
        "brave",
        "opera",
        "vivaldi",
        "arc"
    ];

    private static readonly Dictionary<string, string> KnownDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        ["youtube"] = "youtube.com",
        ["wikipedia"] = "wikipedia.org",
        ["google classroom"] = "classroom.google.com",
        ["google docs"] = "docs.google.com",
        ["google drive"] = "drive.google.com",
        ["scratch"] = "scratch.mit.edu",
        ["spotify"] = "spotify.com",
        ["github"] = "github.com",
        ["stackoverflow"] = "stackoverflow.com",
        ["discord"] = "discord.com",
        ["reddit"] = "reddit.com",
        ["netflix"] = "netflix.com",
        ["twitch"] = "twitch.tv",
        ["x "] = "x.com",
        ["twitter"] = "x.com"
    };

    private static readonly Regex DomainRegex = new(@"(?<![@\w])((?:[a-z0-9-]+\.)+[a-z]{2,})(?:/|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly string[] BrowserSuffixes =
    [
        "Google Chrome",
        "Microsoft Edge",
        "Mozilla Firefox",
        "Brave",
        "Opera",
        "Vivaldi",
        "Arc"
    ];

    public ActivitySample Capture()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return CreateIdleSample();
        }

        GetWindowThreadProcessId(hwnd, out var processId);

        try
        {
            using var process = Process.GetProcessById((int)processId);
            var title = ReadWindowTitle(hwnd);
            var processName = process.ProcessName;
            var appName = GetDisplayName(processName);
            var appCategory = CategorizeApp(processName, title);
            var appSubtitle = string.IsNullOrWhiteSpace(title) ? "Desktop activity in focus" : title;

            string? websiteDomain = null;
            string? websiteCategory = null;
            string? websiteSubtitle = null;

            if (BrowserProcesses.Contains(processName))
            {
                websiteDomain = ExtractDomain(title);
                if (!string.IsNullOrWhiteSpace(websiteDomain))
                {
                    websiteCategory = CategorizeWebsite(websiteDomain);
                    websiteSubtitle = string.IsNullOrWhiteSpace(title) ? websiteDomain : title;
                }
            }

            return new ActivitySample(
                DateTime.Now,
                (int)processId,
                processName,
                appName,
                appCategory,
                appSubtitle,
                websiteDomain,
                websiteCategory,
                websiteSubtitle);
        }
        catch
        {
            return CreateIdleSample();
        }
    }

    private static ActivitySample CreateIdleSample() =>
        new(DateTime.Now, 0, "desktop", "Desktop", "Utility", "No active foreground window detected", null, null, null);

    private static string ReadWindowTitle(IntPtr hwnd)
    {
        var builder = new StringBuilder(512);
        _ = GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString().Trim();
    }

    private static string GetDisplayName(string processName) =>
        processName switch
        {
            "msedge" => "Microsoft Edge",
            "chrome" => "Google Chrome",
            "firefox" => "Firefox",
            "code" => "Visual Studio Code",
            "explorer" => "File Explorer",
            _ => char.ToUpperInvariant(processName[0]) + processName[1..]
        };

    private static string CategorizeApp(string processName, string title)
    {
        if (BrowserProcesses.Contains(processName))
        {
            return "Browser";
        }

        return processName switch
        {
            "code" or "devenv" or "notepad" => "Creative",
            "spotify" => "Music",
            "minecraft" or "robloxplayerbeta" => "Game",
            "explorer" => "Utility",
            _ when title.Contains("Discord", StringComparison.OrdinalIgnoreCase) => "Social",
            _ => "Utility"
        };
    }

    private static string? ExtractDomain(string windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
        {
            return null;
        }

        var match = DomainRegex.Match(windowTitle.ToLowerInvariant());
        if (match.Success)
        {
            return NormalizeDomain(match.Groups[1].Value);
        }

        foreach (var known in KnownDomains)
        {
            if (windowTitle.Contains(known.Key, StringComparison.OrdinalIgnoreCase))
            {
                return known.Value;
            }
        }

        return null;
    }

    private static string NormalizeDomain(string domain) =>
        domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? domain[4..] : domain;

    private static string CategorizeWebsite(string domain)
    {
        if (domain.Contains("classroom") || domain.Contains("wikipedia") || domain.Contains("docs") || domain.Contains("github"))
        {
            return "Learning";
        }

        if (domain.Contains("youtube") || domain.Contains("netflix") || domain.Contains("twitch") || domain.Contains("x.com"))
        {
            return "Video";
        }

        if (domain.Contains("spotify"))
        {
            return "Music";
        }

        if (domain.Contains("scratch"))
        {
            return "Creative";
        }

        if (domain.Contains("reddit") || domain.Contains("discord"))
        {
            return "Social";
        }

        return "General";
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
