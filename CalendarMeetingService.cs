using Windows.ApplicationModel.Appointments;

namespace FocusTrace;

public sealed record CalendarMeetingState(bool IsActive, bool StartsSoon, DateTimeOffset? StartTime);

public sealed class CalendarMeetingService
{
    private AppointmentStore? _store;

    public async Task<CalendarMeetingState> GetStateAsync()
    {
        _store ??= await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly);
        if (_store is null)
        {
            return new CalendarMeetingState(false, false, null);
        }

        DateTimeOffset now = DateTimeOffset.Now;
        IReadOnlyList<Appointment> appointments = await _store.FindAppointmentsAsync(
            now.AddHours(-12),
            TimeSpan.FromHours(24));

        Appointment? active = appointments
            .Where(appointment => appointment.StartTime <= now &&
                                  appointment.StartTime + appointment.Duration > now)
            .OrderBy(appointment => appointment.StartTime)
            .FirstOrDefault();
        if (active is not null)
        {
            return new CalendarMeetingState(true, false, active.StartTime);
        }

        Appointment? upcoming = appointments
            .Where(appointment => appointment.StartTime > now &&
                                  appointment.StartTime <= now.AddMinutes(5))
            .OrderBy(appointment => appointment.StartTime)
            .FirstOrDefault();
        return new CalendarMeetingState(false, upcoming is not null, upcoming?.StartTime);
    }
}
