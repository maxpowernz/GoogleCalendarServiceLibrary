using System;
using System.Collections.Generic;

namespace CalendarServices.GoogleCalendar
{
    public interface IGoogleBookingDetails
    {
        DateTime StartTime { get; set; }
        DateTime EndTime { get; set; }
        string FirstName { get; set; }
        string LastName { get; set; }
        string EmailAddress { get; set; }
        List<Service> SelectedServices { get; set; }
        string PhoneNumber { get; set; }

    }
}
