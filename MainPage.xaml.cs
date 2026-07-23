using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Globalization;
using Windows.UI;

namespace FocusTrace;

public sealed partial class MainPage : Page
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CalendarMeetingService _calendarMeetingService = new();
    private readonly FocusProfileData _profile;
    private FocusDayData _data;
    private string? _lastExternalApp;
    private DateTimeOffset _streakStartedAt = DateTimeOffset.Now;
    private DateTimeOffset? _focusEndsAt;
    private DateTimeOffset? _lastMeetingDistractionReminder;
    private int _timerTicks;
    private int _visibleWindowCount;
    private bool _tracking = true;
    private bool _themeInitializing = true;
    private bool _windowAlertRaised;
    private bool _streakAwardGiven;
    private bool _meetingMode;
    private bool _meetingModeAutomatic;
    private bool _calendarMeetingActive;
    private bool _calendarCheckInProgress;
    private bool _settingsInitializing = true;
    private bool _startupInitializing = true;
    private bool _updatingMeetingToggle;
    private string? _lastCalendarReminderKey;
    private IReadOnlyList<VisibleApplicationInfo> _visibleApplications = [];
    private string _visibleApplicationsKey = string.Empty;

    public MainPage()
    {
        _profile = Application.Current is App app ? app.Profile : FocusDataStore.LoadProfile();
        _data = _profile.GetOrCreateToday();
        InitializeComponent();
        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;
        _timer.Tick += Timer_Tick;

        ThemeComboBox.SelectedIndex = _profile.Theme switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0
        };
        WindowThresholdBox.Value = Math.Clamp(_profile.WindowAlertThreshold, 3, 50);
        ExcludedAppsTextBox.Text = string.Join(", ", _profile.ExcludedApps);
        CalendarDetectionToggle.IsOn = _profile.CalendarMeetingDetectionEnabled;
        TrayModeToggle.IsOn = _profile.TrayModeEnabled;
        NotificationToggle.IsOn = _profile.SystemNotificationsEnabled;
        _themeInitializing = false;
        _settingsInitializing = false;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyTheme(_profile.Theme);
        EvaluateWindowAndMeetingState();
        _ = EvaluateCalendarMeetingStateAsync();
        LoadStartupState();
        RefreshDashboard();
        CheckWeeklyReportNotification();
        _timer.Start();
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        UpdateBestStreak();
        FocusDataStore.Save(_profile);
    }

    private void Timer_Tick(object? sender, object e)
    {
        _timerTicks++;
        EnsureCurrentDay();

        bool userActive = FocusMonitor.IsUserActive(TimeSpan.FromMinutes(2));
        if (_tracking && userActive && _timerTicks % 2 == 0)
        {
            SampleForegroundApplication();
        }

        if (_timerTicks % 2 == 0)
        {
            UpdateVisibleApplications();
        }

        if (_timerTicks % 10 == 0)
        {
            EvaluateWindowAndMeetingState();
        }

        if (_timerTicks % 60 == 0)
        {
            _ = EvaluateCalendarMeetingStateAsync();
        }

        UpdateFocusSession();
        RefreshDashboard();

        if (_timerTicks % 30 == 0)
        {
            UpdateBestStreak();
            FocusDataStore.Save(_profile);
        }

        TrackingStatusText.Text = !_tracking
            ? $"Tracking is paused. {_visibleWindowCount} active windows are open."
            : userActive
                ? $"Tracking active apps while you are working. {_visibleWindowCount} active windows are open."
                : $"Paused automatically while you are away. {_visibleWindowCount} active windows are open.";
    }

    private void SampleForegroundApplication()
    {
        string? appName = FocusMonitor.GetForegroundApplicationName();
        if (string.IsNullOrWhiteSpace(appName))
        {
            return;
        }

        if (_profile.ExcludedApps.Contains(appName, StringComparer.OrdinalIgnoreCase))
        {
            _lastExternalApp = null;
            return;
        }

        if (!_data.Apps.TryGetValue(appName, out AppUsageData? usage))
        {
            usage = new AppUsageData();
            _data.Apps[appName] = usage;
        }

        usage.ActiveSeconds += 2;
        int hour = DateTime.Now.Hour;
        if (!_data.Hours.TryGetValue(hour, out FocusTimeBucketData? hourBucket))
        {
            hourBucket = new FocusTimeBucketData();
            _data.Hours[hour] = hourBucket;
        }

        hourBucket.ActiveSeconds += 2;

        if (_lastExternalApp is not null &&
            !string.Equals(_lastExternalApp, appName, StringComparison.OrdinalIgnoreCase))
        {
            UpdateBestStreak();
            _streakStartedAt = DateTimeOffset.Now;
            _streakAwardGiven = false;
            _data.SwitchCount++;
            usage.Switches++;
            hourBucket.Switches++;

            if (_focusEndsAt is not null)
            {
                _data.FocusInterruptions++;
            }

            if (_meetingMode && !FocusMonitor.IsMeetingApplication(appName))
            {
                RemindMeetingDistraction();
            }

            _data.RecentSwitches.Insert(0, new SwitchEventData
            {
                AppName = appName,
                Timestamp = DateTimeOffset.Now
            });

            if (_data.RecentSwitches.Count > 50)
            {
                _data.RecentSwitches.RemoveRange(50, _data.RecentSwitches.Count - 50);
            }
        }

        _lastExternalApp = appName;
    }

    private void EvaluateWindowAndMeetingState()
    {
        UpdateVisibleApplications();
        int threshold = Math.Clamp(_profile.WindowAlertThreshold, 3, 50);
        if (_visibleWindowCount > threshold && !_windowAlertRaised)
        {
            _windowAlertRaised = true;
            WindowAlertInfoBar.Title = "Too many windows competing for attention";
            WindowAlertInfoBar.Severity = InfoBarSeverity.Warning;
            WindowAlertInfoBar.Message =
                $"{_visibleWindowCount} active windows are open. Close or minimize a few before choosing your next task.";
            WindowAlertInfoBar.IsOpen = true;
            FocusNotificationService.Show(
                "FocusTrace window check",
                $"{_visibleWindowCount} active windows are open. Reduce the visual queue to protect your attention.");
        }
        else if (_visibleWindowCount <= threshold)
        {
            _windowAlertRaised = false;
            WindowAlertInfoBar.Title =
                $"{_visibleWindowCount} active window{(_visibleWindowCount == 1 ? string.Empty : "s")} open";
            WindowAlertInfoBar.Severity = InfoBarSeverity.Informational;
            WindowAlertInfoBar.Message =
                $"You are below your alert threshold of {threshold}. The live app list updates automatically as windows open or close.";
            WindowAlertInfoBar.IsOpen = true;
        }

        string? meetingApp = FocusMonitor.GetRunningMeetingApplication();
        if (meetingApp is not null && !_meetingMode)
        {
            StartMeetingMode(meetingApp, automatic: true);
        }
        else if (meetingApp is null && _meetingModeAutomatic && !_calendarMeetingActive)
        {
            StopMeetingMode();
        }
    }

    private void UpdateVisibleApplications()
    {
        IReadOnlyList<VisibleApplicationInfo> updated = FocusMonitor.GetVisibleApplications();
        _visibleWindowCount = updated.Sum(app => app.WindowCount);
        string updatedKey = string.Join(
            "|",
            updated.Select(app => $"{app.AppName}:{app.WindowCount}"));
        if (updatedKey == _visibleApplicationsKey)
        {
            return;
        }

        _visibleApplications = updated;
        _visibleApplicationsKey = updatedKey;
        RefreshOpenApplications();
    }

    private void RefreshOpenApplications()
    {
        TopAppsList.ItemsSource = _visibleApplications
            .Select(app => new ActiveAppView(
                app.AppName,
                _profile.ExcludedApps.Contains(app.AppName, StringComparer.OrdinalIgnoreCase)
                    ? "Excluded from tracking"
                    : "Open now",
                $"{app.WindowCount} window{(app.WindowCount == 1 ? string.Empty : "s")}"))
            .ToList();
    }

    private async Task EvaluateCalendarMeetingStateAsync()
    {
        if (!_profile.CalendarMeetingDetectionEnabled || _calendarCheckInProgress)
        {
            if (!_profile.CalendarMeetingDetectionEnabled)
            {
                CalendarStatusText.Text = "Calendar detection is off.";
            }
            return;
        }

        _calendarCheckInProgress = true;
        try
        {
            CalendarMeetingState state = await _calendarMeetingService.GetStateAsync();
            CalendarStatusText.Text = state.IsActive
                ? "A calendar meeting is active. Meeting Focus is on."
                : state.StartsSoon
                    ? "A calendar meeting starts within five minutes."
                    : "Calendar connected. No meeting is active.";

            if (state.StartsSoon && state.StartTime is not null)
            {
                string reminderKey = state.StartTime.Value.ToString("O", CultureInfo.InvariantCulture);
                if (_lastCalendarReminderKey != reminderKey)
                {
                    _lastCalendarReminderKey = reminderKey;
                    FocusNotificationService.Show(
                        "Meeting starts soon",
                        "Wrap up the current task and enter the meeting ready to stay present.");
                }
            }

            bool wasActive = _calendarMeetingActive;
            _calendarMeetingActive = state.IsActive;
            if (_calendarMeetingActive && !_meetingMode)
            {
                StartMeetingMode("Calendar meeting", automatic: true);
            }
            else if (wasActive && !_calendarMeetingActive &&
                     FocusMonitor.GetRunningMeetingApplication() is null &&
                     _meetingModeAutomatic)
            {
                StopMeetingMode();
            }
        }
        catch (UnauthorizedAccessException)
        {
            CalendarStatusText.Text = "Calendar access is unavailable. Allow calendar access in Windows Privacy settings.";
        }
        catch
        {
            CalendarStatusText.Text = "Calendar detection is temporarily unavailable.";
        }
        finally
        {
            _calendarCheckInProgress = false;
        }
    }

    private void StartMeetingMode(string? meetingApp, bool automatic)
    {
        _meetingMode = true;
        _meetingModeAutomatic = automatic;
        _lastMeetingDistractionReminder = DateTimeOffset.Now;
        MeetingInfoBar.Title = meetingApp is null
            ? "Meeting Focus is on"
            : $"Meeting Focus: {meetingApp}";
        MeetingInfoBar.IsOpen = true;

        _updatingMeetingToggle = true;
        MeetingFocusButton.IsChecked = true;
        _updatingMeetingToggle = false;

        FocusNotificationService.Show(
            "Stay present in your meeting",
            "Keep the meeting as your primary task. Park unrelated work until the conversation ends.");
    }

    private void StopMeetingMode()
    {
        _meetingMode = false;
        _meetingModeAutomatic = false;
        MeetingInfoBar.IsOpen = false;

        _updatingMeetingToggle = true;
        MeetingFocusButton.IsChecked = false;
        _updatingMeetingToggle = false;
    }

    private void RemindMeetingDistraction()
    {
        if (_lastMeetingDistractionReminder is not null &&
            DateTimeOffset.Now - _lastMeetingDistractionReminder < TimeSpan.FromMinutes(5))
        {
            return;
        }

        _lastMeetingDistractionReminder = DateTimeOffset.Now;
        _data.MeetingDistractions++;
        MeetingInfoBar.Message =
            "You switched away during Meeting Focus. Capture the thought, return to the conversation, and handle it afterward.";
        FocusNotificationService.Show(
            "Return to the meeting",
            "You switched tasks during Meeting Focus. Come back to the conversation and save the other task for later.");
    }

    private void CheckStreakAward(int currentStreakSeconds)
    {
        if (currentStreakSeconds < 300 || currentStreakSeconds <= _profile.AllTimeBestStreakSeconds)
        {
            return;
        }

        _profile.AllTimeBestStreakSeconds = currentStreakSeconds;
        _data.BestStreakSeconds = Math.Max(_data.BestStreakSeconds, currentStreakSeconds);
        if (_streakAwardGiven)
        {
            return;
        }

        _streakAwardGiven = true;
        AwardInfoBar.Message = $"You stayed with one app for {FormatDuration(currentStreakSeconds)}—your longest focus streak yet.";
        AwardInfoBar.IsOpen = true;
        FocusNotificationService.Show(
            "New FocusTrace personal best!",
            $"You just set a {FormatDuration(currentStreakSeconds)} focus streak. Keep the momentum.");
        FocusDataStore.Save(_profile);
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_themeInitializing || ThemeComboBox.SelectedItem is not ComboBoxItem selected)
        {
            return;
        }

        string theme = selected.Tag?.ToString() ?? "System";
        _profile.Theme = theme;
        ApplyTheme(theme);
        RefreshTrends();
        FocusDataStore.Save(_profile);
    }

    private static ElementTheme ParseTheme(string theme) => theme switch
    {
        "Light" => ElementTheme.Light,
        "Dark" => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    private void ApplyTheme(string theme)
    {
        ElementTheme requestedTheme = ParseTheme(theme);
        LayoutRoot.RequestedTheme = requestedTheme;
        if (Application.Current is App app)
        {
            app.MainWindowHost?.ApplyTheme(requestedTheme);
        }
    }

    private void MeetingFocusButton_Checked(object sender, RoutedEventArgs e)
    {
        if (!_updatingMeetingToggle && !_meetingMode)
        {
            StartMeetingMode(null, automatic: false);
        }
    }

    private void MeetingFocusButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!_updatingMeetingToggle && _meetingMode)
        {
            StopMeetingMode();
        }
    }

    private void TrackingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _tracking = TrackingToggle.IsOn;
        if (!_tracking)
        {
            _lastExternalApp = null;
        }
    }

    private void WindowThresholdBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_settingsInitializing || double.IsNaN(args.NewValue))
        {
            return;
        }

        _profile.WindowAlertThreshold = Math.Clamp((int)Math.Round(args.NewValue), 3, 50);
        _windowAlertRaised = false;
        EvaluateWindowAndMeetingState();
        SaveSettings();
    }

    private void SaveExclusionsButton_Click(object sender, RoutedEventArgs e)
    {
        _profile.ExcludedApps = ExcludedAppsTextBox.Text
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();
        ExcludedAppsTextBox.Text = string.Join(", ", _profile.ExcludedApps);
        _lastExternalApp = null;
        SaveSettings();
    }

    private void CalendarDetectionToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_settingsInitializing)
        {
            return;
        }

        _profile.CalendarMeetingDetectionEnabled = CalendarDetectionToggle.IsOn;
        if (!_profile.CalendarMeetingDetectionEnabled)
        {
            _calendarMeetingActive = false;
            CalendarStatusText.Text = "Calendar detection is off.";
        }
        else
        {
            _ = EvaluateCalendarMeetingStateAsync();
        }
        SaveSettings();
    }

    private void TrayModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_settingsInitializing)
        {
            return;
        }

        _profile.TrayModeEnabled = TrayModeToggle.IsOn;
        SaveSettings();
        if (Application.Current is App app)
        {
            app.MainWindowHost?.UpdateTraySettings();
        }
    }

    private void NotificationToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_settingsInitializing)
        {
            return;
        }

        _profile.SystemNotificationsEnabled = NotificationToggle.IsOn;
        FocusNotificationService.Enabled = NotificationToggle.IsOn;
        SaveSettings();
    }

    private void LoadStartupState()
    {
        _startupInitializing = true;
        StartupLaunchToggle.IsOn = StartupRegistrationService.IsEnabled();
        StartupStatusText.Text = StartupLaunchToggle.IsOn
            ? "FocusTrace will start quietly in the notification area after you sign in."
            : "FocusTrace will not launch automatically.";
        _startupInitializing = false;
    }

    private void StartupLaunchToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_startupInitializing)
        {
            return;
        }

        _profile.LaunchAtLoginEnabled = StartupLaunchToggle.IsOn;
        bool succeeded = StartupRegistrationService.SetEnabled(StartupLaunchToggle.IsOn);
        StartupStatusText.Text = succeeded && StartupLaunchToggle.IsOn
            ? "FocusTrace will start quietly in the notification area after you sign in."
            : succeeded
                ? "FocusTrace will not launch automatically."
                : "FocusTrace could not change the Windows startup setting.";
        SaveSettings();
    }

    private void SaveSettings() => FocusDataStore.Save(_profile);

    private void StartFocusButton_Click(object sender, RoutedEventArgs e)
    {
        _focusEndsAt = DateTimeOffset.Now.AddMinutes(25);
        StartFocusButton.IsEnabled = false;
        StopFocusButton.IsEnabled = true;
        FocusSessionInfoBar.IsOpen = true;
        FocusSessionInfoBar.Severity = InfoBarSeverity.Success;
        FocusSessionInfoBar.Title = "Focus session active";
    }

    private void StopFocusButton_Click(object sender, RoutedEventArgs e)
    {
        EndFocusSession(completed: false);
    }

    private async void ResetTodayButton_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog dialog = new()
        {
            Title = "Reset today's focus data?",
            Content = "This clears today's switch count, app timing, and focus sessions. Personal bests and older trend history remain.",
            PrimaryButtonText = "Reset",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            string today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _profile.Days.RemoveAll(day => day.Date == today);
            _data = _profile.GetOrCreateToday();
            _lastExternalApp = null;
            _streakStartedAt = DateTimeOffset.Now;
            FocusDataStore.Save(_profile);
            RefreshDashboard();
        }
    }

    private void UpdateFocusSession()
    {
        if (_focusEndsAt is null)
        {
            return;
        }

        TimeSpan remaining = _focusEndsAt.Value - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
        {
            EndFocusSession(completed: true);
            return;
        }

        FocusTimeRemainingText.Text = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00} remaining";
        FocusProgressBar.Value = 1500 - remaining.TotalSeconds;
    }

    private void EndFocusSession(bool completed)
    {
        _focusEndsAt = null;
        StartFocusButton.IsEnabled = true;
        StopFocusButton.IsEnabled = false;
        FocusProgressBar.Value = 0;

        if (completed)
        {
            _data.FocusSessionsCompleted++;
            _data.FocusMinutes += 25;
            FocusSessionInfoBar.IsOpen = true;
            FocusSessionInfoBar.Severity = InfoBarSeverity.Success;
            FocusSessionInfoBar.Title = "Focus session complete";
            FocusTimeRemainingText.Text = "You protected 25 minutes of focused time.";
            FocusNotificationService.Show(
                "Focus session complete",
                "You protected 25 minutes of focused work. Take a short break before choosing the next task.");
            FocusDataStore.Save(_profile);
        }
        else
        {
            FocusSessionInfoBar.IsOpen = false;
        }
    }

    private void RefreshDashboard()
    {
        int activeSeconds = GetActiveSeconds(_data);
        double activeHours = activeSeconds / 3600d;
        double switchRate = activeHours < 0.05 ? 0 : _data.SwitchCount / activeHours;
        int score = ScoreFromRate(switchRate, _data.FocusInterruptions);
        int currentStreakSeconds = _lastExternalApp is null
            ? 0
            : Math.Max(0, (int)(DateTimeOffset.Now - _streakStartedAt).TotalSeconds);

        CheckStreakAward(currentStreakSeconds);

        SwitchCountText.Text = _data.SwitchCount.ToString(CultureInfo.CurrentCulture);
        SwitchTrendText.Text = _data.FocusSessionsCompleted == 0
            ? "No focus sessions completed yet"
            : $"{_data.FocusSessionsCompleted} focus session{(_data.FocusSessionsCompleted == 1 ? string.Empty : "s")} completed";
        SwitchRateText.Text = switchRate.ToString("0.0", CultureInfo.CurrentCulture);
        FocusScoreText.Text = $"Focus score {score}";
        FocusStreakText.Text = FormatDuration(currentStreakSeconds);
        BestStreakText.Text =
            $"Best today {FormatDuration(Math.Max(_data.BestStreakSeconds, currentStreakSeconds))} · all time {FormatDuration(Math.Max(_profile.AllTimeBestStreakSeconds, currentStreakSeconds))}";

        InsightInfoBar.Message = switchRate switch
        {
            0 when activeSeconds < 180 => "Keep working normally. Your first useful pattern will appear after a few minutes.",
            < 6 => "Your app-switching pace is calm. Protect the streak by finishing the current task before opening another app.",
            < 12 => "Your switching pace is moderate. Try a 25-minute focus session to create a cleaner work block.",
            _ => "Your switching pace is high. Close one nonessential app and choose a single next action."
        };

        RecentSwitchesList.ItemsSource = _data.RecentSwitches
            .Take(8)
            .Select(item => new SwitchEventView(item.AppName, item.Timestamp.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)))
            .ToList();

        if (_timerTicks == 0 || _timerTicks % 10 == 0)
        {
            RefreshTrends();
            RefreshWeeklyReport();
        }
    }

    private void RefreshWeeklyReport()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        (int active, int switches) current = AggregateDateRange(today.AddDays(-6), today);
        (int active, int switches) previous = AggregateDateRange(today.AddDays(-13), today.AddDays(-7));
        int sessions = AggregateDays(today.AddDays(-6), today).Sum(day => day.FocusSessionsCompleted);
        int focusMinutes = AggregateDays(today.AddDays(-6), today).Sum(day => day.FocusMinutes);

        WeeklyActiveTimeText.Text = FormatDuration(current.active);
        WeeklySwitchesText.Text = current.switches.ToString(CultureInfo.CurrentCulture);
        WeeklyFocusBlocksText.Text = sessions.ToString(CultureInfo.CurrentCulture);

        if (current.active < 300)
        {
            WeeklyReportSummaryText.Text = "Keep FocusTrace running during your workday. Your weekly comparison will appear after five active minutes.";
            return;
        }

        double currentRate = current.switches / (current.active / 3600d);
        string comparison;
        if (previous.active < 300)
        {
            comparison = "This is your first complete weekly baseline.";
        }
        else
        {
            double previousRate = previous.switches / (previous.active / 3600d);
            double change = previousRate <= 0 ? 0 : (currentRate - previousRate) / previousRate;
            comparison = Math.Abs(change) < 0.05
                ? "Your switching pace is steady versus the prior week."
                : change < 0
                    ? $"Your switching pace improved by {Math.Abs(change):P0} versus the prior week."
                    : $"Your switching pace increased by {Math.Abs(change):P0} versus the prior week.";
        }

        WeeklyReportSummaryText.Text =
            $"{currentRate:0.0} switches per active hour · {focusMinutes} protected focus minutes. {comparison}";
    }

    private IEnumerable<FocusDayData> AggregateDays(DateOnly start, DateOnly end) =>
        _profile.Days.Where(day =>
            DateOnly.TryParseExact(day.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date) &&
            date >= start &&
            date <= end);

    private void CheckWeeklyReportNotification()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        DateOnly weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        string weekKey = weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (_profile.LastWeeklyReportWeek == weekKey ||
            AggregateDateRange(today.AddDays(-7), today.AddDays(-1)).active < 600)
        {
            return;
        }

        _profile.LastWeeklyReportWeek = weekKey;
        FocusDataStore.Save(_profile);
        FocusNotificationService.Show(
            "Your FocusTrace weekly report is ready",
            "Open FocusTrace to see your focused time, switching pace, and strongest work patterns.");
    }

    private void RefreshTrends()
    {
        List<TrendPeriodView> timePeriods =
        [
            CreateTimePeriodTrend("Morning", 5, 12),
            CreateTimePeriodTrend("Afternoon", 12, 17),
            CreateTimePeriodTrend("Evening", 17, 24)
        ];
        List<TrendPeriodView> weekdays = Enum.GetValues<DayOfWeek>()
            .Select(CreateWeekdayTrend)
            .OrderBy(view => ((int)view.Day + 6) % 7)
            .ToList();
        RenderFocusHeatmap();

        int daysWithData = _profile.Days.Count(day => GetActiveSeconds(day) >= 300);
        TrendPeriodView? bestTime = timePeriods
            .Where(period => period.Rate is not null)
            .MinBy(period => period.Rate);
        TrendPeriodView? bestDay = weekdays
            .Where(period => period.Rate is not null)
            .MinBy(period => period.Rate);

        if (daysWithData < 3 || bestTime is null || bestDay is null)
        {
            TrendSummaryText.Text =
                $"FocusTrace has {daysWithData} useful day{(daysWithData == 1 ? string.Empty : "s")} of data. After three days, it will identify your strongest time and weekday.";
            return;
        }

        string direction = GetRecentDirection();
        TrendSummaryText.Text =
            $"You currently focus best in the {bestTime.Label.ToLowerInvariant()} and on {bestDay.Label}s. {direction}";
    }

    private void RenderFocusHeatmap()
    {
        FocusHeatmapGrid.Children.Clear();
        FocusHeatmapGrid.ColumnDefinitions.Clear();
        FocusHeatmapGrid.RowDefinitions.Clear();

        FocusHeatmapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        for (int column = 0; column < 7; column++)
        {
            FocusHeatmapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        FocusHeatmapGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int row = 0; row < 3; row++)
        {
            FocusHeatmapGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        DayOfWeek[] days =
        [
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday,
            DayOfWeek.Saturday,
            DayOfWeek.Sunday
        ];
        (string Label, int StartHour, int EndHour)[] periods =
        [
            ("Morning", 5, 12),
            ("Afternoon", 12, 17),
            ("Evening", 17, 24)
        ];

        AddHeatmapLabel("Time", 0, 0, isHeader: true);
        for (int dayIndex = 0; dayIndex < days.Length; dayIndex++)
        {
            string dayName = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(days[dayIndex]);
            AddHeatmapLabel(dayName, 0, dayIndex + 1, isHeader: true);
        }

        for (int periodIndex = 0; periodIndex < periods.Length; periodIndex++)
        {
            (string label, int startHour, int endHour) = periods[periodIndex];
            AddHeatmapLabel(label, periodIndex + 1, 0, isHeader: false);
            for (int dayIndex = 0; dayIndex < days.Length; dayIndex++)
            {
                HeatmapBucket bucket = GetHeatmapBucket(days[dayIndex], startHour, endHour);
                AddHeatmapCell(bucket, label, days[dayIndex], periodIndex + 1, dayIndex + 1);
            }
        }
    }

    private void AddHeatmapLabel(string text, int row, int column, bool isHeader)
    {
        TextBlock label = new()
        {
            Text = text,
            Margin = new Thickness(6),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = column == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, column);
        FocusHeatmapGrid.Children.Add(label);
    }

    private void AddHeatmapCell(
        HeatmapBucket bucket,
        string period,
        DayOfWeek day,
        int row,
        int column)
    {
        SolidColorBrush background;
        if (bucket.Score is null)
        {
            background = new SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.12 };
        }
        else
        {
            SolidColorBrush accent = Application.Current.Resources["AccentFillColorDefaultBrush"] as SolidColorBrush
                ?? new SolidColorBrush(Microsoft.UI.Colors.Teal);
            background = new SolidColorBrush(accent.Color)
            {
                Opacity = 0.2 + (bucket.Score.Value / 100d * 0.8)
            };
        }

        TextBlock score = new()
        {
            Text = bucket.Score is null ? "—" : bucket.Score.Value.ToString("0", CultureInfo.CurrentCulture),
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        TextBlock detail = new()
        {
            Text = bucket.Score is null ? "No data" : $"{bucket.SwitchRate:0.0}/hr",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.8
        };
        StackPanel content = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { score, detail }
        };
        Border cell = new()
        {
            MinHeight = 72,
            Margin = new Thickness(4),
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(8),
            Background = background,
            Child = content
        };
        string dayName = CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(day);
        AutomationProperties.SetName(
            cell,
            bucket.Score is null
                ? $"{dayName} {period}: no data"
                : $"{dayName} {period}: focus score {bucket.Score:0}, {bucket.SwitchRate:0.0} switches per hour");
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        FocusHeatmapGrid.Children.Add(cell);
    }

    private HeatmapBucket GetHeatmapBucket(DayOfWeek dayOfWeek, int startHour, int endHour)
    {
        int activeSeconds = 0;
        int switches = 0;
        foreach (FocusDayData day in _profile.Days)
        {
            if (!DateOnly.TryParseExact(day.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date) ||
                date.DayOfWeek != dayOfWeek)
            {
                continue;
            }

            foreach ((int hour, FocusTimeBucketData bucket) in day.Hours)
            {
                if (hour >= startHour && hour < endHour)
                {
                    activeSeconds += bucket.ActiveSeconds;
                    switches += bucket.Switches;
                }
            }
        }

        if (activeSeconds < 300)
        {
            return new HeatmapBucket(null, null);
        }

        double rate = switches / (activeSeconds / 3600d);
        return new HeatmapBucket(ScoreFromRate(rate, 0), rate);
    }

    private TrendPeriodView CreateTimePeriodTrend(string label, int startHour, int endHour)
    {
        int activeSeconds = 0;
        int switches = 0;
        foreach (FocusDayData day in _profile.Days)
        {
            foreach ((int hour, FocusTimeBucketData bucket) in day.Hours)
            {
                if (hour >= startHour && hour < endHour)
                {
                    activeSeconds += bucket.ActiveSeconds;
                    switches += bucket.Switches;
                }
            }
        }

        return CreateTrendView(label, activeSeconds, switches);
    }

    private TrendPeriodView CreateWeekdayTrend(DayOfWeek dayOfWeek)
    {
        int activeSeconds = 0;
        int switches = 0;
        foreach (FocusDayData day in _profile.Days)
        {
            if (!DateOnly.TryParseExact(day.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date) ||
                date.DayOfWeek != dayOfWeek)
            {
                continue;
            }

            activeSeconds += GetActiveSeconds(day);
            switches += day.SwitchCount;
        }

        TrendPeriodView view = CreateTrendView(
            CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(dayOfWeek),
            activeSeconds,
            switches);
        return view with { Day = dayOfWeek };
    }

    private static TrendPeriodView CreateTrendView(string label, int activeSeconds, int switches)
    {
        if (activeSeconds < 300)
        {
            return new TrendPeriodView(label, 0, "Collecting…", null, DayOfWeek.Sunday);
        }

        double rate = switches / (activeSeconds / 3600d);
        return new TrendPeriodView(
            label,
            ScoreFromRate(rate, 0),
            $"{rate:0.0}/hr",
            rate,
            DayOfWeek.Sunday);
    }

    private string GetRecentDirection()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        (int active, int switches) recent = AggregateDateRange(today.AddDays(-6), today);
        (int active, int switches) previous = AggregateDateRange(today.AddDays(-13), today.AddDays(-7));
        if (recent.active < 600 || previous.active < 600)
        {
            return "Keep collecting data to reveal your week-over-week direction.";
        }

        double recentRate = recent.switches / (recent.active / 3600d);
        double previousRate = previous.switches / (previous.active / 3600d);
        double change = (recentRate - previousRate) / previousRate;
        if (Math.Abs(change) < 0.05)
        {
            return "Your switch rate is steady compared with the prior week.";
        }

        return change < 0
            ? $"Your switch rate improved by {Math.Abs(change):P0} versus the prior week."
            : $"Your switch rate rose by {Math.Abs(change):P0} versus the prior week.";
    }

    private (int active, int switches) AggregateDateRange(DateOnly start, DateOnly end)
    {
        int active = 0;
        int switches = 0;
        foreach (FocusDayData day in _profile.Days)
        {
            if (DateOnly.TryParseExact(day.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date) &&
                date >= start &&
                date <= end)
            {
                active += GetActiveSeconds(day);
                switches += day.SwitchCount;
            }
        }

        return (active, switches);
    }

    private void UpdateBestStreak()
    {
        if (_lastExternalApp is null)
        {
            return;
        }

        int seconds = Math.Max(0, (int)(DateTimeOffset.Now - _streakStartedAt).TotalSeconds);
        _data.BestStreakSeconds = Math.Max(_data.BestStreakSeconds, seconds);
        _profile.AllTimeBestStreakSeconds = Math.Max(_profile.AllTimeBestStreakSeconds, seconds);
    }

    private void EnsureCurrentDay()
    {
        string today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (_data.Date == today)
        {
            return;
        }

        UpdateBestStreak();
        FocusDataStore.Save(_profile);
        _data = _profile.GetOrCreateToday();
        _lastExternalApp = null;
        _streakStartedAt = DateTimeOffset.Now;
        _streakAwardGiven = false;
    }

    private static int GetActiveSeconds(FocusDayData day)
    {
        int hourlySeconds = day.Hours.Values.Sum(bucket => bucket.ActiveSeconds);
        return hourlySeconds > 0 ? hourlySeconds : day.Apps.Values.Sum(app => app.ActiveSeconds);
    }

    private static int ScoreFromRate(double rate, int interruptions) =>
        Math.Clamp((int)Math.Round(100 - (rate * 3) - (interruptions * 2)), 0, 100);

    private static string FormatDuration(int totalSeconds)
    {
        TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes}m";
        }

        return $"{duration.Seconds}s";
    }
}

public sealed record SwitchEventView(string AppName, string Time);
public sealed record ActiveAppView(string AppName, string Detail, string Windows);
public sealed record HeatmapBucket(double? Score, double? SwitchRate);
public sealed record TrendPeriodView(
    string Label,
    double Score,
    string Detail,
    double? Rate,
    DayOfWeek Day);
