using System;
using System.Collections.Generic;

namespace CalendarServices
{
    public interface IBookingDetails
    {
        DateTime StartTime { get; set; }
        DateTime EndTime { get; set; }
        string FirstName { get; set; }
        string LastName { get; set; }
        string EmailAddress { get; set; }
        string PhoneNumber { get; set; }
        //string IsPaid { get; set; }

        IEnumerable<IService> Services { get; set; }


    }
}
