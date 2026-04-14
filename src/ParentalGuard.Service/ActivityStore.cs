using System.IO;
using Microsoft.Data.Sqlite;

namespace ParentalGuard.Service;

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
            RecordUsage(connection, sample.ObservedAt.Date, "website", NormalizeWebsite(sample.WebsiteDomain!), sample.WebsiteCategory ?? "General", sample.WebsiteSubtitle ?? sample.WebsiteDomain!, 1);
        }
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

    public int GetUsageSecondsForTodayForAppRule(string targetKey)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT item_key, subtitle, seconds
            FROM usage_records
            WHERE usage_date = $date AND item_type = 'app';
            """;
        command.Parameters.AddWithValue("$date", DateTime.Today.ToString("yyyy-MM-dd"));

        using var reader = command.ExecuteReader();
        var totalSeconds = 0;
        while (reader.Read())
        {
            var itemKey = reader.GetString(0);
            var subtitle = reader.GetString(1);
            var seconds = reader.GetInt32(2);
            if (MatchesAppRule(targetKey, itemKey, subtitle))
            {
                totalSeconds += seconds;
            }
        }

        return totalSeconds;
    }

    public List<BlockRuleRecord> LoadBlockRules()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT target_type, target_key, display_name, is_enabled, max_minutes, COALESCE(list_type, 'blocked') FROM block_rules ORDER BY list_type, target_type, display_name;";
        using var reader = command.ExecuteReader();
        var rules = new List<BlockRuleRecord>();
        while (reader.Read())
        {
            rules.Add(new BlockRuleRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3) == 1, reader.GetInt32(4), reader.GetString(5)));
        }
        return rules;
    }

    public bool EnsureRuleExists(string targetType, string targetKey, string displayName, int maxMinutes = 30, string listType = "blocked")
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var existsCommand = connection.CreateCommand();
        existsCommand.CommandText = "SELECT COUNT(1) FROM block_rules WHERE target_type = $type AND target_key = $key;";
        existsCommand.Parameters.AddWithValue("$type", targetType);
        existsCommand.Parameters.AddWithValue("$key", targetKey);
        if (Convert.ToInt32(existsCommand.ExecuteScalar()) > 0)
        {
            return false;
        }

        using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText = "INSERT INTO block_rules (target_type, target_key, display_name, is_enabled, max_minutes, list_type) VALUES ($type, $key, $display, 1, $minutes, $list);";
        insertCommand.Parameters.AddWithValue("$type", targetType);
        insertCommand.Parameters.AddWithValue("$key", targetKey);
        insertCommand.Parameters.AddWithValue("$display", displayName);
        insertCommand.Parameters.AddWithValue("$minutes", maxMinutes);
        insertCommand.Parameters.AddWithValue("$list", listType);
        insertCommand.ExecuteNonQuery();
        return true;
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

    private void EnsureDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var usageCommand = connection.CreateCommand();
        usageCommand.CommandText =
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
        usageCommand.ExecuteNonQuery();

        using var rulesCommand = connection.CreateCommand();
        rulesCommand.CommandText =
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
        rulesCommand.ExecuteNonQuery();

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
            DO UPDATE SET subtitle = excluded.subtitle, category = excluded.category, seconds = usage_records.seconds + excluded.seconds;
            """;
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$subtitle", subtitle);
        command.Parameters.AddWithValue("$seconds", seconds);
        command.ExecuteNonQuery();
    }

    private static string NormalizeWebsite(string domain) =>
        domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? domain[4..].ToLowerInvariant() : domain.ToLowerInvariant();

    private static bool MatchesAppRule(string targetKey, params string[] values)
    {
        var normalizedRule = NormalizeAppKey(targetKey);
        var candidates = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NormalizeAppKey(value))
            .ToArray();

        return normalizedRule switch
        {
            "discord" => candidates.Any(value => value.Contains("discord")),
            "youtube" => candidates.Any(value => value.Contains("youtube")),
            "twitter" => candidates.Any(value => value.Contains("twitter") || value == "x"),
            _ => candidates.Any(value => value.Contains(normalizedRule))
        };
    }

    private static string NormalizeAppKey(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized.EndsWith(".exe", StringComparison.Ordinal) ? normalized[..^4] : normalized;
    }
}
