# Changelog

Notable FocusTrace changes are documented here by release date.

## 2026-07-24

### Added

- A **Reset all focus data** action under settings. It clears history, trends, focus sessions, switch events, and streak records while preserving preferences.
- A Meeting Focus app selector that chooses which visible app remains open when Meeting Focus begins.
- Safe Meeting Focus window cleanup: other visible windows are minimized only when the selected app is currently visible.
- An inactive schedule with selectable weekdays, start and end times, and support for overnight ranges.

### Changed

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
