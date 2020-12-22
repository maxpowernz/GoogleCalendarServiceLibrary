using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace CalendarServices.GoogleCalendar.Models
{
    public class WorkDay
    {
        public int Id { get; set; }
        public DayOfWeek DayOfWeek { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        [Display(Name = "NoWork")]
        public bool IsNonWorkingDay { get; set; } = true;
    }

}