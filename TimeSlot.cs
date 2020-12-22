using System;

namespace CalendarServices.GoogleCalendar
{

    public enum TimeSlotStatusEnum
    {
        Reserved,
        Open,
        Closed
    }

    public class TimeSlot : IEquatable<TimeSlot>, IComparable<TimeSlot>
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSlotStatusEnum Status { get; set; } = TimeSlotStatusEnum.Open;
        //public string Summary { get; set; } = "None";
        public bool IsOutSideInterval = false;

        public int CompareTo(TimeSlot other)
        {
            // A null value means that this object is greater.
            if (other == null)
            {
                return 1;
            }
            else
            {
                return StartTime.CompareTo(other.StartTime);
            }
        }

        public bool Equals(TimeSlot other)
        {
            return StartTime == other.StartTime ? true : false;
        }

    }
}
