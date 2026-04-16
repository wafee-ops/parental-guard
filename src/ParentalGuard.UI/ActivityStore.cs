using System.IO;
using Microsoft.Data.Sqlite;

namespace ParentalGuard.UI;

public sealed class ActivityStore
{
    private readonly string _connectionString;

    public ActivityStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        EnsureDatabase();
        RemoveLegacyDemoRulesIfPresent();
    }

    public void RecordSample(ActivitySample sample)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        RecordUsage(connection, sample.ObservedAt.Date, "app", sample.AppName, sample.AppCategory, sample.AppSubtitle, 1);
        RecordHourlyUsage(connection, sample.ObservedAt, "app", sample.AppName, sample.AppCategory, sample.AppSubtitle, 1);
        if (!string.IsNullOrWhiteSpace(sample.WebsiteDomain))
        {
            RecordUsage(
                connection,
                sample.ObservedAt.Date,
                "website",
                sample.WebsiteDomain!,
                sample.WebsiteCategory ?? "General",
                sample.WebsiteSubtitle ?? sample.WebsiteDomain!,
                1);
            RecordHourlyUsage(
                connection,
                sample.ObservedAt,
                "website",
                sample.WebsiteDomain!,
                sample.WebsiteCategory ?? "General",
                sample.WebsiteSubtitle ?? sample.WebsiteDomain!,
                1);
        }
    }

    public List<UsageEntry> LoadCombinedUsageForRange(DateTime startDate, DateTime endDate, string categoryFilter)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT usage_date, item_type, item_key, category, subtitle, seconds
            FROM usage_records
            WHERE usage_date >= $start AND usage_date <= $end
            ORDER BY usage_date DESC, seconds DESC, item_key ASC;
            """;
        command.Parameters.AddWithValue("$start", startDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$end", endDate.ToString("yyyy-MM-dd"));

        using var reader = command.ExecuteReader();
        var aggregates = new Dictionary<string, UsageAggregate>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            var itemType = reader.GetString(1);
            var key = reader.GetString(2);
            var normalizedCategory = NormalizeCategory(reader.GetString(3));
            if (!MatchesCategoryFilter(normalizedCategory, categoryFilter))
            {
                continue;
            }

            var subtitle = reader.GetString(4);
            var seconds = reader.GetInt32(5);
            var aggregateKey = $"{itemType}:{key}";
            if (!aggregates.TryGetValue(aggregateKey, out var aggregate))
            {
                aggregate = new UsageAggregate(
                    key,
                    normalizedCategory,
                    subtitle,
                    CreateAccentBrush(normalizedCategory));
                aggregates[aggregateKey] = aggregate;
            }

            aggregate.Seconds += seconds;
        }

        return aggregates.Values
            .OrderByDescending(item => item.Seconds)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new UsageEntry(item.Name, item.Category, item.Seconds, 9999, item.Subtitle, item.AccentBrush))
            .ToList();
    }

    public List<UsageEntry> LoadCombinedUsageForDate(DateTime date)
    {
        return LoadCombinedUsageForRange(date, date, "All Categories");
    }

    public List<LineGraphPoint> LoadDailyLinePoints(int days, double width, double height)
    {
        var today = DateTime.Today;
        var totals = new Dictionary<string, int>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT usage_date, COALESCE(SUM(seconds), 0)
            FROM usage_records
            WHERE usage_date >= $start
            GROUP BY usage_date
            ORDER BY usage_date ASC;
            """;
        command.Parameters.AddWithValue("$start", today.AddDays(-(days - 1)).ToString("yyyy-MM-dd"));

        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                totals[reader.GetString(0)] = reader.GetInt32(1);
            }
        }

        var rows = new List<(DateTime date, int seconds)>();
        var maxSeconds = 1;
        for (var i = days - 1; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            var seconds = totals.GetValueOrDefault(date.ToString("yyyy-MM-dd"), 0);
            maxSeconds = Math.Max(maxSeconds, seconds);
            rows.Add((date, seconds));
        }

        var gap = rows.Count > 1 ? width / (rows.Count - 1) : width;
        return rows
            .Select((row, index) => new LineGraphPoint
            {
                Label = row.date.ToString("ddd"),
                DurationText = FormatDuration(row.seconds),
                X = index * gap,
                Y = height - (row.seconds / (double)maxSeconds * height)
            })
            .ToList();
    }

    public int GetTotalUsageSeconds(DateTime date)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COALESCE(SUM(seconds), 0)
            FROM usage_records
            WHERE usage_date = $date;
            """;
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));

        var result = command.ExecuteScalar();
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    public int GetTotalUsageSecondsInRange(DateTime startDate, DateTime endDate)
    {
        return GetTotalUsageSecondsInRange(startDate, endDate, "All Categories");
    }

    public int GetTotalUsageSecondsInRange(DateTime startDate, DateTime endDate, string categoryFilter)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT category, seconds
            FROM usage_records
            WHERE usage_date >= $start AND usage_date <= $end;
            """;
        command.Parameters.AddWithValue("$start", startDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$end", endDate.ToString("yyyy-MM-dd"));

        using var reader = command.ExecuteReader();
        var total = 0;
        while (reader.Read())
        {
            var category = NormalizeCategory(reader.GetString(0));
            if (!MatchesCategoryFilter(category, categoryFilter))
            {
                continue;
            }

            total += reader.GetInt32(1);
        }

        return total;
    }

    public List<HourlyUsageBar> LoadHourlyUsageBars(DateTime startDate, DateTime endDate, string categoryFilter, double maxHeight)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT usage_hour, category, seconds
            FROM usage_hour_records
            WHERE usage_date >= $start AND usage_date <= $end
            ORDER BY usage_hour ASC;
            """;
        command.Parameters.AddWithValue("$start", startDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$end", endDate.ToString("yyyy-MM-dd"));

        var totals = Enumerable.Range(0, 24).ToDictionary(hour => hour, _ => 0);
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var hourStamp = reader.GetString(0);
                var category = NormalizeCategory(reader.GetString(1));
                if (!MatchesCategoryFilter(category, categoryFilter))
                {
                    continue;
                }

                if (!DateTime.TryParse(hourStamp, out var parsedHour))
                {
                    continue;
                }

                totals[parsedHour.Hour] += reader.GetInt32(2);
            }
        }

        var maxSeconds = Math.Max(1, totals.Values.Max());
        var peakHour = totals.OrderByDescending(pair => pair.Value).First().Key;

        return totals
            .Select(pair => new HourlyUsageBar
            {
                Label = pair.Key % 4 == 0 ? FormatHourLabel(pair.Key) : string.Empty,
                AccessibilityLabel = $"{FormatHourLabel(pair.Key)}: {FormatDuration(pair.Value)}",
                Seconds = pair.Value,
                Height = pair.Value == 0 ? 8 : Math.Max(8, pair.Value / (double)maxSeconds * maxHeight),
                FillBrush = pair.Key == peakHour && pair.Value > 0 ? CreateBrush("#71E4C3") : CreateBrush("#3A87F6"),
                DurationText = FormatDuration(pair.Value)
            })
            .ToList();
    }

    public int GetUsageSecondsForToday(string targetType, string targetKey)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COALESCE(seconds, 0)
            FROM usage_records
            WHERE usage_date = $date AND item_type = $type AND item_key = $key;
            """;
        command.Parameters.AddWithValue("$date", DateTime.Today.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$type", targetType);
        command.Parameters.AddWithValue("$key", targetKey);

        var result = command.ExecuteScalar();
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    public List<BlockRule> LoadBlockRules()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT target_type, target_key, display_name, is_enabled, max_minutes, COALESCE(list_type, 'blocked')
            FROM block_rules
            ORDER BY list_type, target_type, display_name;
            """;

        using var reader = command.ExecuteReader();
        var rules = new List<BlockRule>();
        while (reader.Read())
        {
            rules.Add(new BlockRule
            {
                TargetType = reader.GetString(0),
                TargetKey = reader.GetString(1),
                DisplayName = reader.GetString(2),
                IsEnabled = reader.GetInt32(3) == 1,
                MaxMinutes = reader.GetInt32(4),
                ListType = reader.GetString(5)
            });
        }

        return rules;
    }

    public void SaveBlockRules(IEnumerable<BlockRule> rules)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        foreach (var rule in rules)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO block_rules (target_type, target_key, display_name, is_enabled, max_minutes, list_type)
                VALUES ($type, $key, $display, $enabled, $minutes, $list)
                ON CONFLICT(target_type, target_key)
                DO UPDATE SET
                    display_name = excluded.display_name,
                    is_enabled = excluded.is_enabled,
                    max_minutes = excluded.max_minutes,
                    list_type = excluded.list_type;
                """;
            command.Parameters.AddWithValue("$type", rule.TargetType);
            command.Parameters.AddWithValue("$key", NormalizeRuleKey(rule.TargetType, rule.TargetKey));
            command.Parameters.AddWithValue("$display", rule.DisplayName);
            command.Parameters.AddWithValue("$enabled", rule.IsEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$minutes", rule.MaxMinutes);
            command.Parameters.AddWithValue("$list", rule.ListType);
            command.ExecuteNonQuery();
        }
    }

    public bool EnsureRuleExists(string targetType, string targetKey, string displayName, int maxMinutes = 30, string listType = "blocked")
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var existsCommand = connection.CreateCommand();
        existsCommand.CommandText =
            """
            SELECT COUNT(1)
            FROM block_rules
            WHERE target_type = $type AND target_key = $key;
            """;
        existsCommand.Parameters.AddWithValue("$type", targetType);
        existsCommand.Parameters.AddWithValue("$key", NormalizeRuleKey(targetType, targetKey));

        var exists = Convert.ToInt32(existsCommand.ExecuteScalar()) > 0;
        if (exists)
        {
            return false;
        }

        using var insertCommand = connection.CreateCommand();
            insertCommand.CommandText =
                """
                INSERT INTO block_rules (target_type, target_key, display_name, is_enabled, max_minutes, list_type)
                VALUES ($type, $key, $display, 1, $minutes, $list);
                """;
            insertCommand.Parameters.AddWithValue("$type", targetType);
            insertCommand.Parameters.AddWithValue("$key", NormalizeRuleKey(targetType, targetKey));
            insertCommand.Parameters.AddWithValue("$display", displayName);
            insertCommand.Parameters.AddWithValue("$minutes", maxMinutes);
            insertCommand.Parameters.AddWithValue("$list", listType);
        insertCommand.ExecuteNonQuery();
        return true;
    }

    public void DeleteBlockRule(string targetType, string targetKey)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM block_rules
            WHERE target_type = $type AND target_key = $key;
            """;
        command.Parameters.AddWithValue("$type", targetType);
        command.Parameters.AddWithValue("$key", NormalizeRuleKey(targetType, targetKey));
        command.ExecuteNonQuery();
    }

    public void DeleteAllBlockRules()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM block_rules;";
        command.ExecuteNonQuery();
    }

    public void SetListType(string targetType, string targetKey, string listType)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE block_rules SET list_type = $list WHERE target_type = $type AND target_key = $key;";
        command.Parameters.AddWithValue("$list", listType);
        command.Parameters.AddWithValue("$type", targetType);
        command.Parameters.AddWithValue("$key", NormalizeRuleKey(targetType, targetKey));
        command.ExecuteNonQuery();
    }

    private void EnsureDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS usage_records (
                    usage_date TEXT NOT NULL,
                    item_type TEXT NOT NULL,
                    item_key TEXT NOT NULL,
                    category TEXT NOT NULL,
                    subtitle TEXT NOT NULL,
                    seconds INTEGER NOT NULL,
                    PRIMARY KEY (usage_date, item_type, item_key)
                );
                """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS usage_hour_records (
                    usage_hour TEXT NOT NULL,
                    usage_date TEXT NOT NULL,
                    item_type TEXT NOT NULL,
                    item_key TEXT NOT NULL,
                    category TEXT NOT NULL,
                    subtitle TEXT NOT NULL,
                    seconds INTEGER NOT NULL,
                    PRIMARY KEY (usage_hour, item_type, item_key)
                );
                """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS block_rules (
                    target_type TEXT NOT NULL,
                    target_key TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    is_enabled INTEGER NOT NULL,
                    max_minutes INTEGER NOT NULL,
                    PRIMARY KEY (target_type, target_key)
                );
                """;
            command.ExecuteNonQuery();
        }

        try
        {
            using var migrateCommand = connection.CreateCommand();
            migrateCommand.CommandText = "ALTER TABLE block_rules ADD COLUMN list_type TEXT NOT NULL DEFAULT 'blocked';";
            migrateCommand.ExecuteNonQuery();
        }
        catch { }
    }

    private void RemoveLegacyDemoRulesIfPresent()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(1) FROM block_rules;";
        var totalCount = Convert.ToInt32(countCommand.ExecuteScalar());
        if (totalCount != LegacyDemoRules.Count)
        {
            return;
        }

        using var matchesCommand = connection.CreateCommand();
        matchesCommand.CommandText =
            $"""
            SELECT COUNT(1)
            FROM block_rules
            WHERE (target_type, target_key) IN ({string.Join(", ", LegacyDemoRules.Select((_, index) => $"($type{index}, $key{index})"))});
            """;

        for (var i = 0; i < LegacyDemoRules.Count; i++)
        {
            matchesCommand.Parameters.AddWithValue($"$type{i}", LegacyDemoRules[i].TargetType);
            matchesCommand.Parameters.AddWithValue($"$key{i}", LegacyDemoRules[i].TargetKey);
        }

        var matchedCount = Convert.ToInt32(matchesCommand.ExecuteScalar());
        if (matchedCount != LegacyDemoRules.Count)
        {
            return;
        }

        using var deleteCommand = connection.CreateCommand();
        deleteCommand.CommandText = "DELETE FROM block_rules;";
        deleteCommand.ExecuteNonQuery();
    }

    private static void RecordUsage(SqliteConnection connection, DateTime date, string type, string key, string category, string subtitle, int seconds)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO usage_records (usage_date, item_type, item_key, category, subtitle, seconds)
            VALUES ($date, $type, $key, $category, $subtitle, $seconds)
            ON CONFLICT(usage_date, item_type, item_key)
            DO UPDATE SET
                subtitle = excluded.subtitle,
                category = excluded.category,
                seconds = usage_records.seconds + excluded.seconds;
            """;
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$subtitle", subtitle);
        command.Parameters.AddWithValue("$seconds", seconds);
        command.ExecuteNonQuery();
    }

    private static void RecordHourlyUsage(SqliteConnection connection, DateTime observedAt, string type, string key, string category, string subtitle, int seconds)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO usage_hour_records (usage_hour, usage_date, item_type, item_key, category, subtitle, seconds)
            VALUES ($hour, $date, $type, $key, $category, $subtitle, $seconds)
            ON CONFLICT(usage_hour, item_type, item_key)
            DO UPDATE SET
                subtitle = excluded.subtitle,
                category = excluded.category,
                seconds = usage_hour_records.seconds + excluded.seconds;
            """;
        command.Parameters.AddWithValue("$hour", observedAt.ToString("yyyy-MM-dd HH:00:00"));
        command.Parameters.AddWithValue("$date", observedAt.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$subtitle", subtitle);
        command.Parameters.AddWithValue("$seconds", seconds);
        command.ExecuteNonQuery();
    }

    private static string NormalizeRuleKey(string targetType, string key)
    {
        var normalized = key.Trim().ToLowerInvariant();
        if (targetType == "website" && normalized.StartsWith("www.", StringComparison.Ordinal))
        {
            normalized = normalized[4..];
        }

        if (targetType == "app" && normalized.EndsWith(".exe", StringComparison.Ordinal))
        {
            normalized = normalized[..^4];
        }

        return normalized;
    }

    private static readonly IReadOnlyList<(string TargetType, string TargetKey)> LegacyDemoRules =
    [
        ("website", "youtube.com"),
        ("website", "x.com"),
        ("website", "discord.com"),
        ("app", "youtube"),
        ("app", "twitter"),
        ("app", "discord")
    ];

    private static System.Windows.Media.SolidColorBrush CreateBrush(string hex) =>
        (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFromString(hex)!;

    private static string NormalizeCategory(string category) =>
        category.Trim() switch
        {
            "Game" or "Gaming" => "Gaming",
            "Social" or "Social Media" => "Social Media",
            "Browser" or "Browsing" => "Browsing",
            "Creative" or "Productivity" => "Productivity",
            "Learning" => "Learning",
            "Music" or "Video" or "Entertainment" => "Entertainment",
            "Utility" or "Utilities" => "Utilities",
            "General" => "General",
            _ => "General"
        };

    private static bool MatchesCategoryFilter(string normalizedCategory, string categoryFilter) =>
        categoryFilter == "All Categories" || normalizedCategory.Equals(categoryFilter, StringComparison.OrdinalIgnoreCase);

    private static System.Windows.Media.SolidColorBrush CreateAccentBrush(string category) =>
        category switch
        {
            "Gaming" => CreateBrush("#FF8A72"),
            "Social Media" => CreateBrush("#B287FF"),
            "Productivity" => CreateBrush("#71D7FF"),
            "Entertainment" => CreateBrush("#FFD58A"),
            "Browsing" => CreateBrush("#6BB8FF"),
            "Utilities" => CreateBrush("#71E4C3"),
            "Learning" => CreateBrush("#8BE28B"),
            _ => CreateBrush("#9DB4C8")
        };

    private static string FormatHourLabel(int hour)
    {
        var suffix = hour >= 12 ? "PM" : "AM";
        var normalizedHour = hour % 12;
        if (normalizedHour == 0)
        {
            normalizedHour = 12;
        }

        return $"{normalizedHour}{suffix}";
    }

    private static string FormatDuration(int seconds)
    {
        if (seconds < 60)
        {
            return $"{Math.Max(1, seconds)}s";
        }

        var duration = TimeSpan.FromSeconds(seconds);
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes:00}m";
        }

        return $"{duration.Minutes}m";
    }

    private sealed class UsageAggregate(string name, string category, string subtitle, System.Windows.Media.SolidColorBrush accentBrush)
    {
        public string Name { get; } = name;

        public string Category { get; } = category;

        public string Subtitle { get; } = subtitle;

        public System.Windows.Media.SolidColorBrush AccentBrush { get; } = accentBrush;

        public int Seconds { get; set; }
    }
}
