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
        EnsureDefaultRules();
    }

    public void RecordSample(ActivitySample sample)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        RecordUsage(connection, sample.ObservedAt.Date, "app", sample.AppName, sample.AppCategory, sample.AppSubtitle, 1);
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
        }
    }

    public List<UsageEntry> LoadCombinedUsageForDate(DateTime date)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT item_type, item_key, category, subtitle, seconds
            FROM usage_records
            WHERE usage_date = $date
            ORDER BY seconds DESC, item_key ASC;
            """;
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));

        using var reader = command.ExecuteReader();
        var items = new List<UsageEntry>();
        while (reader.Read())
        {
            var itemType = reader.GetString(0);
            var key = reader.GetString(1);
            var category = reader.GetString(2);
            var subtitle = reader.GetString(3);
            var seconds = reader.GetInt32(4);

            items.Add(new UsageEntry(
                key,
                category,
                seconds,
                9999,
                subtitle,
                itemType == "website" ? CreateBrush("#3FD0AE") : CreateBrush("#6BB8FF")));
        }

        return items;
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
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COALESCE(SUM(seconds), 0)
            FROM usage_records
            WHERE usage_date >= $start AND usage_date <= $end;
            """;
        command.Parameters.AddWithValue("$start", startDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$end", endDate.ToString("yyyy-MM-dd"));

        var result = command.ExecuteScalar();
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
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
            command.Parameters.AddWithValue("$key", rule.TargetKey);
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
        existsCommand.Parameters.AddWithValue("$key", targetKey);

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
            insertCommand.Parameters.AddWithValue("$key", targetKey);
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
        command.Parameters.AddWithValue("$key", targetKey);
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
        command.Parameters.AddWithValue("$key", targetKey);
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

    private void EnsureDefaultRules()
    {
        EnsureRuleExists("website", "youtube.com", "youtube.com", 60, "allowed");
        EnsureRuleExists("website", "x.com", "x.com", 30, "allowed");
        EnsureRuleExists("website", "discord.com", "discord.com", 30, "allowed");
        EnsureRuleExists("app", "youtube", "YouTube app", 60, "allowed");
        EnsureRuleExists("app", "twitter", "Twitter/X app", 30, "allowed");
        EnsureRuleExists("app", "discord", "Discord app", 30, "allowed");
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

    private static System.Windows.Media.SolidColorBrush CreateBrush(string hex) =>
        (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFromString(hex)!;

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
}
