using System;
using System.Collections.Generic;
using System.Text;

namespace CalendarServices.GoogleCalendar
{
    public class GoogleBookingDetails
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public List<string> SelectedServices { get; set; }
        public string Phone { get; set; }
        public int ReminderTime { get; set; }

    }
}
