using CalendarServices.GoogleCalendar;
using CalendarServices.GoogleCalendar.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using System;
using System.Collections.Generic;

namespace CalendarServices
{
    public interface ICalendarService
    {

        IList<Event> GetEvents(DateTime eventStartDate, DateTime eventEndDate);
        IList<TimeSlot> BuildCalendar(DateTime startDate, DateTime endDate, IList<Event> calendarEvents, double interval = 15);
        IList<TimeSlot> GetAvailableTimes(IList<TimeSlot> workShift, int serviceDuration);
        IEnumerable<DateTime> GetBookedOutDays(List<WorkDayDto> workDays, DateTime timeMin, DateTime timeMax, TimeSpan interval);
        IEnumerable<TimePeriod> GetFreeTimeSlots(List<WorkDayDto> workDays, DateTime timeMin, DateTime timeMax);

        IList<object> CalendarDebug { get; set; }

        TimeZoneInfo TimeZone { get; set; }

        CalendarService _calendarService { get; set; }

        CalendarOptions Options { get; }

        ServiceAccountCredential credential { get; set; }

        void PrintAllTimes(IList<TimeSlot> workShift);
        string CreateEvent(IBookingDetails bookingDetail);

        string DeleteEvent(string eventId);
    }
}
