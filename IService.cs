using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace CalendarServices
{
    public interface IService
    {
        int ServiceId { get; set; }
        string Name { get; set; }

    }
}
