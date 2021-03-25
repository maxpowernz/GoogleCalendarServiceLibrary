using System;
using System.ComponentModel.DataAnnotations;

namespace CalendarServices.GoogleCalendar.Models
{
    public class WorkDayDto
    {
        public DayOfWeek DayOfWeek { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public bool IsNonWorkingDay { get; set; } = true;
    }

}