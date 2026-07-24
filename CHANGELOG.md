# Changelog

Notable FocusTrace changes are documented here by release date.

## 2026-07-24

### Added

- An x64 WiX MSI project and repeatable `build-msi.ps1` build command.
- A GitHub Actions workflow that builds and uploads an MSI manually or for `v*` release tags.
- Explicit `1.0.0` application, assembly, file, and installer version metadata.
- A compact mini mode with live focus score, streak, active-window count, current app, timer, and Meeting Focus controls.
- Safe per-app close controls for visible applications. FocusTrace sends a normal close request so applications can still prompt to save work.
- An in-app upcoming-meeting prompt that can turn on Meeting Focus directly.
- A **Reset all focus data** action under settings. It clears history, trends, focus sessions, switch events, and streak records while preserving preferences.
- A Meeting Focus app selector that chooses which visible app remains open when Meeting Focus begins.
- Safe Meeting Focus window cleanup: other visible windows are minimized only when the selected app is currently visible.
- An inactive schedule with selectable weekdays, start and end times, and support for overnight ranges.

### Changed

- FocusTrace now uses an unpackaged, framework-dependent launch model so direct executable and `dotnet run` launches are reliable.
- Packaged-only notification registration is skipped when package identity is unavailable, preventing startup from blocking before the main window appears.
- Settings now open from a gear button in the upper-right corner.
- Theme selection moved into the settings panel.
- Calendar notifications now explicitly remind you to set Meeting Focus before an upcoming meeting.
- Hidden, tray-only, and minimized application windows no longer count as open or active.
- Applications begin counting again when their visible window is restored or opened from the notification area.
- Meeting-app detection ignores hidden and minimized meeting windows.
- Project documentation now covers focus scheduling and Meeting Focus window cleanup.

## 2026-07-23

### Added

- Initial public FocusTrace release.
- Live active-application and visible-window monitoring.
- Context-switch counts, focus scores, streak tracking, and personal-best awards.
- Twenty-five-minute focus sessions.
- Configurable window-count alerts and application exclusions.
- Calendar-aware and application-aware Meeting Focus reminders.
- Weekly reports and a day/time focus heat map.
- Light, dark, and system themes.
- Notification-area mode, Windows alerts, and optional launch at sign-in.
- Local-only focus-history persistence.
