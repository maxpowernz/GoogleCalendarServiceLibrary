namespace CalendarServices.GoogleCalendar
{
    public class CalendarOptions
    {

        public string CalendarId { get; set; }
        public string CalendarAccountSettings { get; set; }
        public string CalendarTimeZone { get; set; }
        public string ApplicationName { get; set; }
        public double WorkShiftInterval { get; set; }
        public double SameDayHourOffset { get; set; }

    }
}
