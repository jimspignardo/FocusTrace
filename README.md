# FocusTrace

FocusTrace is a privacy-first Windows desktop app that makes context switching visible. It watches which application is active while you are using your PC, highlights distraction patterns, and helps you protect longer stretches of focused work.

![FocusTrace app icon](Assets/StoreLogo.png)

## What's new — July 24, 2026

- **Reset everything:** Clear all focus history, trends, sessions, switch events, and streak records while keeping your settings.
- **Cleaner meetings:** Choose which app remains visible when Meeting Focus starts; other visible windows are minimized.
- **Inactive schedules:** Select days and times when FocusTrace should pause, including overnight ranges.
- **More accurate app counts:** Hidden, tray-only, and minimized apps no longer count as open or active. They count again when their window is restored.

See the [full changelog](CHANGELOG.md) for the complete release history.

## Features

- Live active-app and visible-window monitoring
- Automatic inactivity pause when you step away
- Context-switch counts, active time, and current focus streak
- 25-minute focus sessions
- Personal-best focus streak awards
- Configurable alerts when too many windows are open
- App exclusions for tools that should not affect focus data
- Inactive schedules with selectable days and overnight time ranges
- Meeting Focus reminders for meeting apps and calendar events
- Meeting Focus window cleanup that keeps a selected app visible
- Weekly summaries and a day/time focus heat map
- Light, dark, and system themes
- Notification-area mode and Windows alerts
- Optional launch at sign-in
- Per-day and complete focus-history reset controls

## Privacy

FocusTrace processes activity locally and does not upload focus history. It records application names and aggregate timing data, not window titles, document contents, keystrokes, screenshots, or browsing history.

Focus history is stored at:

```text
%LOCALAPPDATA%\FocusTrace\focus-data.json
```

Calendar access is read-only and is used only to determine whether a meeting is active or about to begin.

## Requirements

- Windows 10 version 1809 or later
- .NET 10 SDK
- Windows App SDK 1.8

The project targets x86, x64, and ARM64.

## Build and run

Clone the repository, open a terminal in the project directory, and run:

```powershell
dotnet restore
dotnet build
dotnet run
```

You can also open `FocusTrace.csproj` in Visual Studio with the Windows application development workload installed.

## Project status

FocusTrace is an early desktop release. Core activity tracking, trends, focus sessions, window alerts, and local persistence are implemented. Launch-at-sign-in behavior may vary between packaged and unpackaged development builds and is still being refined.

See the [changelog](CHANGELOG.md) for release history and recent improvements.

## Contributing

Issues and pull requests are welcome. Please avoid including personal `focus-data.json` files, build output, or signing certificates in contributions.

## License

FocusTrace is available under the [MIT License](LICENSE).
