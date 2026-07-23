using System.Globalization;
using System.Text.Json;

namespace FocusTrace;

public sealed class FocusProfileData
{
    public string Theme { get; set; } = "System";
    public int WindowAlertThreshold { get; set; } = 12;
    public List<string> ExcludedApps { get; set; } = [];
    public bool CalendarMeetingDetectionEnabled { get; set; } = true;
    public bool TrayModeEnabled { get; set; } = true;
    public bool LaunchAtLoginEnabled { get; set; } = true;
    public bool SystemNotificationsEnabled { get; set; } = true;
    public string LastWeeklyReportWeek { get; set; } = string.Empty;
    public int AllTimeBestStreakSeconds { get; set; }
    public List<FocusDayData> Days { get; set; } = [];

    public FocusDayData GetOrCreateToday()
    {
        string today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        FocusDayData? existing = Days.FirstOrDefault(day => day.Date == today);
        if (existing is not null)
        {
            return existing;
        }

        FocusDayData created = FocusDayData.CreateToday();
        Days.Add(created);
        Days = Days
            .OrderByDescending(day => day.Date)
            .Take(90)
            .OrderBy(day => day.Date)
            .ToList();
        return created;
    }
}

public sealed class FocusDayData
{
    public string Date { get; set; } = string.Empty;
    public int SwitchCount { get; set; }
    public int FocusSessionsCompleted { get; set; }
    public int FocusInterruptions { get; set; }
    public int FocusMinutes { get; set; }
    public int MeetingDistractions { get; set; }
    public int BestStreakSeconds { get; set; }
    public Dictionary<string, AppUsageData> Apps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<int, FocusTimeBucketData> Hours { get; set; } = [];
    public List<SwitchEventData> RecentSwitches { get; set; } = [];

    public static FocusDayData CreateToday() => new()
    {
        Date = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
    };
}

public sealed class FocusTimeBucketData
{
    public int ActiveSeconds { get; set; }
    public int Switches { get; set; }
}

public sealed class AppUsageData
{
    public int ActiveSeconds { get; set; }
    public int Switches { get; set; }
}

public sealed class SwitchEventData
{
    public string AppName { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}

public static class FocusDataStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FocusTrace");
    private static readonly string DataPath = Path.Combine(DataDirectory, "focus-data.json");

    public static FocusProfileData LoadProfile()
    {
        try
        {
            if (!File.Exists(DataPath))
            {
                return new FocusProfileData();
            }

            string json = File.ReadAllText(DataPath);
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty(nameof(FocusProfileData.Days), out _))
            {
                FocusProfileData profile =
                    JsonSerializer.Deserialize<FocusProfileData>(json, SerializerOptions) ?? new FocusProfileData();
                if (!document.RootElement.TryGetProperty(nameof(FocusProfileData.LaunchAtLoginEnabled), out _))
                {
                    profile.LaunchAtLoginEnabled = true;
                }

                return profile;
            }

            FocusDayData? legacyDay = JsonSerializer.Deserialize<FocusDayData>(json, SerializerOptions);
            return legacyDay is null
                ? new FocusProfileData()
                : new FocusProfileData
                {
                    AllTimeBestStreakSeconds = legacyDay.BestStreakSeconds,
                    Days = [legacyDay]
                };
        }
        catch
        {
            return new FocusProfileData();
        }
    }

    public static void Save(FocusProfileData profile)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            string temporaryPath = DataPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(profile, SerializerOptions));
            File.Move(temporaryPath, DataPath, overwrite: true);
        }
        catch
        {
            // Tracking should continue even if local persistence is temporarily unavailable.
        }
    }
}
