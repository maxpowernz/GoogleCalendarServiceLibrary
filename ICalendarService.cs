using CalendarServices.GoogleCalendar;
using CalendarServices.GoogleCalendar.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CalendarServices
{
    public interface ICalendarService
    {

        IList<Event> GetEvents(DateTime eventStartDate, DateTime eventEndDate);
        IList<TimeSlot> BuildCalendar(DateTime startDate, DateTime endDate, IList<Event> calendarEvents, double interval = 15);
        IList<TimeSlot> GetAvailableTimes(IList<TimeSlot> workShift, int serviceDuration);
        //IEnumerable<TimePeriod> GetBookedDays(DateTime timeMin, DateTime timeMax, string calendarId, List<WorkDay> workDays);
        IEnumerable<DateTime> GetBookedOutDays(DateTime timeMin, DateTime timeMax, List<WorkDay> workDays, TimeSpan interval);
        IEnumerable<TimePeriod> GetFreeTimeSlots(IEnumerable<IGrouping<DateTime, Event>> eventsGroup, List<WorkDay> workDays, TimeSpan interval, DateTime timeMin, DateTime timeMax);


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
