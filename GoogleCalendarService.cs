using CalendarServices.GoogleCalendar.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using static Google.Apis.Http.ConfigurableMessageHandler;

namespace CalendarServices.GoogleCalendar
{
    class SkewClock : IClock
    {
        public DateTime Now => DateTime.Now.AddMinutes(-10);
        public DateTime UtcNow => DateTime.UtcNow.AddMinutes(-10);
    }


    public class GoogleCalendarService : ICalendarService
    {

        public IList<object> CalendarDebug { get; set; } = new List<object>();

        public TimeZoneInfo TimeZone { get; set; }
        public CalendarOptions Options { get; }
        public CalendarService _calendarService { get; set; }
        public ServiceAccountCredential credential { get; set; }

        public GoogleCalendarService(IOptions<CalendarOptions> options)
        {

            Options = options.Value;
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById(Options.CalendarTimeZone);

            GetCalendarService();
        }


        private void GetCalendarService()
        {

            string[] scopes = { CalendarService.Scope.Calendar };

            using (var stream = new FileStream(Options.CalendarAccountSettings, FileMode.Open, FileAccess.Read))
            {
                var config = Google.Apis.Json.NewtonsoftJsonSerializer.Instance.Deserialize<JsonCredentialParameters>(stream);

                credential = new ServiceAccountCredential(
                   new ServiceAccountCredential.Initializer(config.ClientEmail)
                   {
                       Scopes = scopes,
                       Clock = new SkewClock()
                   }
                   .FromPrivateKey(config.PrivateKey))
                {
                    Token = new Google.Apis.Auth.OAuth2.Responses.TokenResponse()
                };
            }

            var service = new CalendarService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = Options.ApplicationName,

            });

            CalendarDebug.Add($"{nameof(Options.ApplicationName)} {Options.ApplicationName}");

            _calendarService = service;

            //_calendarService.HttpClient.MessageHandler.LogEvents = LogEventType.RequestHeaders | LogEventType.ResponseHeaders | LogEventType.RequestUri | LogEventType.ResponseBody | LogEventType.ResponseAbnormal;
        }

        public IList<Event> GetEvents(DateTime eventStartDate, DateTime eventEndDate)
        {

            _calendarService.HttpClient.MessageHandler.LogEvents = LogEventType.RequestHeaders | LogEventType.ResponseHeaders | LogEventType.RequestUri | LogEventType.ResponseBody | LogEventType.ResponseAbnormal;
            CalendarDebug.Clear();


            IList<Event> calendarEvents;

            var eventsResource = new EventsResource.ListRequest(_calendarService, Options.CalendarId)
            {
                OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime,
                SingleEvents = true, // need this for orderby to work
                ShowDeleted = false,
                MaxResults = 100,
                TimeMin = TimeZoneInfo.ConvertTimeToUtc(eventStartDate, TimeZone),
                TimeMax = TimeZoneInfo.ConvertTimeToUtc(eventEndDate, TimeZone)

            };

            Events events = eventsResource.Execute();
            calendarEvents = events.Items;

            return calendarEvents;

        }

        public IList<TimeSlot> BuildCalendar(DateTime startDate, DateTime endDate, IList<Event> calendarEvents, double interval = 15)
        {

            var workShift = new List<TimeSlot>();

            CalendarDebug.Add("Building calendar");
            CalendarDebug.Add($"startdate: {startDate} enddate: {endDate}");


            while (DateTime.Compare(startDate, endDate) < 0)
            {
                workShift.Add(new TimeSlot { StartTime = startDate, EndTime = startDate.AddMinutes(interval) });
                startDate = startDate.AddMinutes(interval);
            }

            //if workshift is empty return empty workshift list
            if (workShift.Count == 0)
            {
                return workShift;
            }

            DateTime workShiftEndDate = workShift.Last().EndTime;

            //add reserved events to workShift
            foreach (var eventItem in calendarEvents)
            {

                //convert times to timezone
                eventItem.Start.DateTimeRaw = TimeZoneInfo.ConvertTime(DateTime.Parse(eventItem.Start.DateTimeRaw), TimeZone).ToString();
                eventItem.End.DateTimeRaw = TimeZoneInfo.ConvertTime(DateTime.Parse(eventItem.End.DateTimeRaw), TimeZone).ToString();

                //find reserved slots in external calendar and set them to reserved in our calendar
                IEnumerable<TimeSlot> reservedSlots = workShift.Where(s => (s.StartTime >= eventItem.Start.DateTime && s.StartTime < eventItem.End.DateTime));

                //find and set all start times in events to reserved in workShift
                foreach (TimeSlot timeSlot in reservedSlots)
                {
                    TimeSlot reserved = workShift.FirstOrDefault(t => t.StartTime == timeSlot.StartTime);
                    reserved.Status = TimeSlotStatusEnum.Reserved;
                }


                //find any start times that dont match the workShift slots
                if (workShift.Where(s => s.StartTime == eventItem.Start.DateTime).Count() == 0)
                {

                    workShift.Add(new TimeSlot
                    {
                        StartTime = eventItem.Start.DateTime.Value,
                        EndTime = eventItem.End.DateTime.Value,
                        Status = TimeSlotStatusEnum.Reserved,
                        IsOutSideInterval = true

                    });
                }

                //break out if we have reached end of workShift
                if (eventItem.End.DateTime >= workShiftEndDate)
                {
                    break;
                }

                //find and add any end times that dont match the workShift slots
                if (workShift.Where(s => s.StartTime == eventItem.End.DateTime).Count() == 0)
                {
                    workShift.Add(new TimeSlot
                    {
                        StartTime = eventItem.End.DateTime.Value,
                        EndTime = eventItem.End.DateTime.Value,
                        Status = TimeSlotStatusEnum.Open,
                        IsOutSideInterval = true
                    });
                }
            }


            //loop updated workShift and set endtimes to match ones found in events
            foreach (var currentSlot in workShift)
            {
                //get next start time
                var next = workShift.FirstOrDefault(s => s.StartTime > currentSlot.StartTime);

                if (next != null)
                {
                    currentSlot.EndTime = next.StartTime;
                }

            }

            return workShift.OrderBy(d => d.StartTime).ToList();

        }

        public void PrintAllTimes(IList<TimeSlot> workShift)
        {
            string hmt = "hh:mmtt";

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("start of work day");
            Console.ResetColor();

            foreach (var timeSlot in workShift)
            {

                Console.ForegroundColor = timeSlot.Status == TimeSlotStatusEnum.Open ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine($"Start: {timeSlot.StartTime.ToString(hmt)} End: {timeSlot.EndTime.ToString(hmt)} {timeSlot.Status} {timeSlot.IsOutSideInterval}");
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("end of work day");
            Console.ResetColor();

        }

        public IList<TimeSlot> GetAvailableTimes(IList<TimeSlot> workShift, int serviceDuration)
        {


            IList<TimeSlot> availableTimes = new List<TimeSlot>();

            foreach (var timeSlot in workShift)
            {

                var availableSlot = workShift
                    .Where(s => s.EndTime >= timeSlot.StartTime.AddMinutes(serviceDuration))
                    .Where(s => s.Status == TimeSlotStatusEnum.Open && timeSlot.Status == TimeSlotStatusEnum.Open)
                    .FirstOrDefault();

                //need to loop back through slots to see if any in between are reserved.
                if (availableSlot != null)
                {

                    //check to see if theres any slots between start and end that are reserved
                    //could do it a different way by making each open time slot into a block.
                    var timeSlotJump = workShift.Where(s => s.StartTime >= timeSlot.StartTime)
                                     .Where(s => s.StartTime < availableSlot.EndTime)
                                     .Where(s => s.Status == TimeSlotStatusEnum.Reserved).FirstOrDefault();

                    //if the slot is ok add it to the list
                    if (timeSlotJump == null)
                    {
                        availableTimes.Add(new TimeSlot
                        {
                            StartTime = timeSlot.StartTime,
                            EndTime = availableSlot.EndTime
                        });
                    }
                }

            }

            return availableTimes;
        }

        public string CreateEvent(IBookingDetails bookingDetails)
        {

            //Event.ExtendedPropertiesData extendedProperties = new Event.ExtendedPropertiesData
            //{
            //    Private__ = new Dictionary<string, string>()
            //};
            //extendedProperties.Private__.Add("isPaid", bookingDetails.IsPaid);

            var eventsResource = new EventsResource(_calendarService);

            string selectedServices = string.Join(", ", bookingDetails.Services.Select(s => s.Name));

            try
            {
                Event calendarEvent = new Event()
                {

                    Start = new EventDateTime() { DateTime = TimeZoneInfo.ConvertTimeToUtc(bookingDetails.StartTime, TimeZone) },
                    End = new EventDateTime() { DateTime = TimeZoneInfo.ConvertTimeToUtc(bookingDetails.EndTime, TimeZone) },
                    Summary = $"{bookingDetails.FirstName} {bookingDetails.LastName} ({bookingDetails.PhoneNumber}) {selectedServices}",
                    Description = $"Booked at {DateTime.Now.ToLongDateString()}, {DateTime.Now.ToLongTimeString()}\n{bookingDetails.PhoneNumber}\n{selectedServices}\n{bookingDetails.EmailAddress}"
                };

                var result = eventsResource.Insert(calendarEvent, Options.CalendarId).Execute();

                return result.Id;
            }

            catch (Exception ex)
            {
                Console.WriteLine(ex.StackTrace);
                throw new Exception(ex.Message);
            }
        }

        public string DeleteEvent(string eventId)
        {


            var resource = new EventsResource.DeleteRequest(_calendarService, Options.CalendarId, eventId);
            var result = resource.Execute();

            return result;

        }

        /// <summary>
        /// Get booked out days for a year
        /// </summary>
        /// <param name="workDays"></param>
        /// <param name="interval"></param>
        /// <returns></returns>
        public IEnumerable<DateTime> GetBookedOutDays(DateTime timeMin, DateTime timeMax, List<WorkDay> workDays, TimeSpan interval)
        {

            List<DateTime> BookedOutDays = new List<DateTime>(0);

            var nonWorkDays = workDays.Where(d => d.IsNonWorkingDay).ToList();

            var request = new EventsResource.ListRequest(_calendarService, Options.CalendarId)
            {
                TimeMin = timeMin,
                TimeMax = timeMax,
                TimeZone = "New Zealand Standard"

            };

            var result = request.Execute();

            //select all day events, they return null as datetime
            var nonFilteredEvents = result.Items
                .Where(d => d.Start.DateTime == null)
                .Select(d => DateTime.Parse(d.Start.Date));

            //filter out non working days
            var allDayEvents = nonFilteredEvents
                .Where(d => d.DayOfWeek != nonWorkDays.Find(e => e.DayOfWeek == d.DayOfWeek)?.DayOfWeek);
                            

            BookedOutDays.AddRange(allDayEvents);


            //group the events by year, month, day
            var eventsGroup = result.Items.Where(d => d.Start.DateTime != null)
                .GroupBy(t => new DateTime(t.Start.DateTime.Value.Year, t.Start.DateTime.Value.Month, t.Start.DateTime.Value.Day))
                .OrderBy(t => (t.Key.Year, t.Key.Month, t.Key.Day));


            //if there is only one event for the day check if it spans the whole day
            var singleEvents = eventsGroup.Where(g => g.Count() == 1).Select(g => new Event
            {
                Start = g.FirstOrDefault().Start,
                End = g.FirstOrDefault().End
            });


            foreach (var singleEvent in singleEvents)
            {
                var currentDay = singleEvent.Start.DateTime.Value;
                var currentDayOfWeek = currentDay.DayOfWeek;
                var workDay = workDays.FirstOrDefault(d => d.DayOfWeek == currentDayOfWeek);
                var nonWorkDay = workDays.SingleOrDefault(d => d.DayOfWeek == currentDayOfWeek).IsNonWorkingDay;

                if (nonWorkDay) continue;

                var workDayStartTime = new DateTime(currentDay.Year, currentDay.Month, currentDay.Day) + workDay.StartTime;
                var workDayEndTime = new DateTime(currentDay.Year, currentDay.Month, currentDay.Day) + workDay.EndTime;

                if (allDayEvents.Any(d => d.Date == currentDay.Date))
                {
                    continue;
                }

                //this will be an event that lasts all day
                if (singleEvent.Start.DateTime.Value.Hour <= workDayStartTime.Hour && singleEvent.End.DateTime.Value.Hour >= workDayEndTime.Hour)
                {
                   BookedOutDays.Add((DateTime)singleEvent.Start.DateTime);              
                }
            }


            var freeTimeSlots = GetFreeTimeSlots(eventsGroup, workDays, interval, timeMin, timeMax);


            //bool noFreeTimePeriods = false;
            //check to see if selected interval is available

            var x = freeTimeSlots.Where(t => t.End - t.Start < interval);

            foreach(var t in x) 
            {
                Debug.WriteLine($"{t.Start} {t.End}");
            }


            //foreach (var freeTime in freeTimePeriods)
            //{
            //    if (freeTime.End - freeTime.Start < interval)
            //    {
            //        noFreeTimePeriods = true;
            //    }
            //    else
            //    {
            //        noFreeTimePeriods = false;
            //    }
            //}

            //if (noFreeTimePeriods == true)
            //{
            //    //BookedOutDays.Add(new TimePeriod { Start = currentDay });
            //}



            return BookedOutDays.OrderBy(d => d);
        }


        /// <summary>
        /// Get free time slots, can either provide an events group or null to make a new request
        /// </summary>
        /// <param name="eventsGroup"></param>
        /// <param name="workDays"></param>
        /// <param name="interval"></param>
        /// <param name="timeMin"></param>
        /// <param name="timeMax"></param>
        /// <returns></returns>
        public IEnumerable<TimePeriod> GetFreeTimeSlots(IEnumerable<IGrouping<DateTime, Event>> eventsGroup, List<WorkDay> workDays, TimeSpan interval, DateTime timeMin, DateTime timeMax)
        {

            var freeTimeSlots = new List<TimePeriod>(0);
            IEnumerable<DateTime> allDayEvents = new List<DateTime>(0);

            //make new request if null events group passed in as param
            if (eventsGroup == null)
            {

                var request = new EventsResource.ListRequest(_calendarService, Options.CalendarId)
                {
                    TimeMin = timeMin,
                    TimeMax = timeMax,
                    TimeZone = "New Zealand Standard"

                };

                var result = request.Execute();

                eventsGroup = result.Items.Where(d => d.Start.DateTime != null)
                                    .OrderBy(t => (t.Start.DateTime))
                                    .GroupBy(t => new DateTime(t.Start.DateTime.Value.Year, t.Start.DateTime.Value.Month, t.Start.DateTime.Value.Day));

                allDayEvents = result.Items.Where(d => d.Start.DateTime == null).Select(d => DateTime.Parse(d.Start.Date)).ToList();
            }


            // get days that have no events and add them to the list
            // need to check all day events as well and exclude them
            var eventsList = eventsGroup.SelectMany(e => e).ToList();

            while (timeMax >= timeMin)
            {

                //if current date is all day event continue
                if (allDayEvents.Any(d => d.ToShortDateString() == timeMin.ToShortDateString()))
                {
                    timeMin = timeMin.AddDays(1);
                    continue;
                };

                var isDayInEventList = eventsList.Any(d => d.Start.DateTime.Value.ToShortDateString() == timeMin.ToShortDateString());

                if (!isDayInEventList)
                {

                    if (workDays.Any(d => d.DayOfWeek == timeMin.DayOfWeek && d.IsNonWorkingDay == false))
                    {
                        var dayOfWeek = timeMin.DayOfWeek;
                        var startTime = new DateTime(timeMin.Year, timeMin.Month, timeMin.Day) + workDays.Single(d => d.DayOfWeek == dayOfWeek).StartTime;
                        var endTime = new DateTime(timeMin.Year, timeMin.Month, timeMin.Day) + workDays.Single(d => d.DayOfWeek == dayOfWeek).EndTime;

                        freeTimeSlots.Add(new TimePeriod
                        {
                            Start = startTime,
                            End = endTime
                        });
                    }
                }

                timeMin = timeMin.AddDays(1);
            }


            //get free slots 
            foreach (IGrouping<DateTime, Event> events in eventsGroup)
            {

                var dayOfWeek = events.Key.DayOfWeek;
                var currentDay = events.FirstOrDefault().Start.DateTime.Value;
                var currentDayOfWeek = currentDay.DayOfWeek;
                var nonWorkDay = workDays.FirstOrDefault(d => d.DayOfWeek == currentDayOfWeek)?.IsNonWorkingDay ?? true;

                if (nonWorkDay) continue;

                var workDayEndTime = new DateTime(currentDay.Year, currentDay.Month, currentDay.Day) + workDays.FirstOrDefault(d => d.DayOfWeek == dayOfWeek).EndTime;
                var workDayStartTime = new DateTime(currentDay.Year, currentDay.Month, currentDay.Day) + workDays.FirstOrDefault(d => d.DayOfWeek == dayOfWeek).StartTime;

                //add slot if first event starttime is greater than workday starttime
                var firstTimeSlot = events.FirstOrDefault(e => events.FirstOrDefault().Start.DateTime.Value > workDayStartTime);

                if (firstTimeSlot != null)
                {
                    freeTimeSlots.Add(new TimePeriod
                    {
                        Start = workDayStartTime,
                        End = firstTimeSlot.Start.DateTime > workDayEndTime ? workDayEndTime : firstTimeSlot.Start.DateTime
                    });
                }


                ////select all the other free time slots
                var timeSlots = events
                            .Where(e => e.Start.DateTime.Value.Hour < workDayEndTime.Hour)
                            .Select((time, index) => new TimePeriod
                            {
                                Start = time.End.DateTime,
                                End = events.Skip(index + 1).FirstOrDefault()?.Start.DateTime.Value ?? workDayEndTime
                            }).Where(t => t.Start != t.End);


                if (timeSlots.LastOrDefault()?.End.Value.Hour > workDayEndTime.Hour)
                {
                    freeTimeSlots.Add(new TimePeriod
                    {
                        Start = timeSlots.Last().Start.Value,
                        End = workDayEndTime
                    });
                }
                else
                {
                    freeTimeSlots.AddRange(timeSlots);
                }

            }

            return freeTimeSlots.OrderBy(d => d.Start);
        }
    }
}
