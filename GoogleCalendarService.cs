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
        public IEnumerable<DateTime> GetBookedOutDays(List<WorkDayDto> workDays, DateTime timeMin, DateTime timeMax, TimeSpan interval)
        {

            List<DateTime> BookedOutDays = new List<DateTime>(0);

            var freeTimeSlots = GetFreeTimeSlots(workDays, timeMin, timeMax);
            var freeTimeSlotsGrouped = freeTimeSlots.GroupBy(d => (d.Start.Value.Year, d.Start.Value.Month, d.Start.Value.Day));

            string zoneId = Options.CalendarTimeZone;
            TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);

            var localTime = TimeZoneInfo.ConvertTime(timeMin, timeZone);
            timeMin = new DateTime(localTime.Year, localTime.Month, localTime.Day);


            //if (DateTime.Now.ToShortDateString() != timeMin.ToShortDateString())
            //{
            //    BookedOutDays.Add(DateTime.Now);
            //}


            //check time slot interval, if there is none greater than interval then add to list
            foreach (var freeTimeSlotsGroup in freeTimeSlotsGrouped)
            {
                if (!freeTimeSlotsGroup.Any(d => d.End - d.Start >= interval))
                {
                    BookedOutDays.Add(freeTimeSlotsGroup.FirstOrDefault().Start.Value);
                }
            }


            var firstDayEndTime = new DateTime(timeMin.Year, timeMin.Month, timeMin.Day) + workDays.Single(d => d.DayOfWeek == timeMin.DayOfWeek).EndTime;
            var firstDayHourOffset = TimeZoneInfo.ConvertTime(timeMin.AddHours(Options.SameDayHourOffset), timeZone);

            // if no slots left greater than datetime offset book out day
            var firstDayFreeSlots = freeTimeSlots.Any(d => d.Start.Value.ToShortDateString() == timeMin.ToShortDateString()
                                        && d.End >= firstDayHourOffset
                                        && d.End - d.Start >= interval);


            if (!firstDayFreeSlots)
            {
                BookedOutDays.Add(timeMin);
            }


            //loop through days and add days that are not in freetime slots to list
            while (timeMax >= timeMin)
            {

                if (!freeTimeSlots.Any(d => d.Start?.ToShortDateString() == timeMin.ToShortDateString()) && !BookedOutDays.Contains(timeMin))
                {
                    BookedOutDays.Add(timeMin);
                }

                timeMin = timeMin.AddDays(1);
            }

            return BookedOutDays.OrderBy(d => d).ToList();
        }


        /// <summary>
        /// Get free time slots
        /// </summary>
        /// <param name="eventsGroup"></param>
        /// <param name="workDays"></param>
        /// <param name="interval"></param>
        /// <param name="timeMin"></param>
        /// <param name="timeMax"></param>
        /// <returns></returns>
        public IEnumerable<TimePeriod> GetFreeTimeSlots(List<WorkDayDto> workDays, DateTime timeMin, DateTime timeMax)
        {

            string zoneId = Options.CalendarTimeZone;
            TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            var firstDate = TimeZoneInfo.ConvertTime(timeMin, timeZone);

            var freeTimeSlots = new List<TimePeriod>(0);

            var request = new EventsResource.ListRequest(_calendarService, Options.CalendarId)
            {
                TimeMin = timeMin,
                TimeMax = timeMax,
                ShowDeleted = false,
                SingleEvents = true,
                MaxResults = 2500
            };

            var result = request.Execute();


            //important, convert start and end to calendar time zone
            var eventsGroup = result.Items.Where(d => d.Start.DateTime != null)
                            .OrderBy(t => (t.Start.DateTime, t.End.DateTime))
                            .Select(e => new Event
                            {
                                Id = e.Id,
                                Start = new EventDateTime { DateTime = TimeZoneInfo.ConvertTime(e.Start.DateTime.Value, timeZone) },
                                End = new EventDateTime { DateTime = TimeZoneInfo.ConvertTime(e.End.DateTime.Value, timeZone) }
                            })
                            .GroupBy(t => new DateTime(t.Start.DateTime.Value.Year, t.Start.DateTime.Value.Month, t.Start.DateTime.Value.Day)).ToList();


            var allDayEvents = result.Items
                                      .Where(d => d.Start.DateTime == null)
                                      .Select(d => new Event
                                      {
                                          Start = new EventDateTime()
                                          {
                                              DateTime = DateTime.Parse(d.Start.Date)
                                          },
                                          End = new EventDateTime()
                                          {
                                              DateTime = DateTime.Parse(d.End.Date)
                                          }
                                      }).OrderBy(d => d.Start.DateTime);


            var allDayEventsStart = allDayEvents.FirstOrDefault()?.Start.DateTime.Value;
            var allDayEventsEnd = allDayEvents.FirstOrDefault()?.End.DateTime.Value;

            //flatten list to save repeated query
            var eventsFlattened = eventsGroup.SelectMany(g => g).ToList();


            // get days that have no events
            while (timeMax > timeMin)
            {

                // if day has no events then it is free
                var dayHasEvents = eventsFlattened.Any(d => d.Start.DateTime.Value.ToShortDateString() == timeMin.ToShortDateString());
                var dayOfWeek = timeMin.DayOfWeek;
                var startTime = new DateTime(timeMin.Year, timeMin.Month, timeMin.Day) + workDays.Single(d => d.DayOfWeek == dayOfWeek).StartTime;
                var endTime = new DateTime(timeMin.Year, timeMin.Month, timeMin.Day) + workDays.Single(d => d.DayOfWeek == dayOfWeek).EndTime;


                if (!dayHasEvents && firstDate < endTime)
                {
                    if (workDays.Any(d => d.DayOfWeek == timeMin.DayOfWeek && d.IsNonWorkingDay == false))
                    {
                        freeTimeSlots.Add(new TimePeriod
                        {
                            Start = startTime,
                            End = endTime
                        });
                    }
                }

                timeMin = timeMin.AddDays(1);
            }


            //process all events
            foreach (IGrouping<DateTime, Event> events in eventsGroup)
            {

                var dayOfWeek = events.Key.DayOfWeek;
                var currentDay = events.Key;
                var nonWorkDay = workDays.FirstOrDefault(d => d.DayOfWeek == dayOfWeek)?.IsNonWorkingDay ?? true;

                if (nonWorkDay) continue;

                var workDayStartTime = new DateTime(currentDay.Year, currentDay.Month, currentDay.Day) + workDays.FirstOrDefault(d => d.DayOfWeek == dayOfWeek).StartTime;
                var workDayEndTime = new DateTime(currentDay.Year, currentDay.Month, currentDay.Day) + workDays.FirstOrDefault(d => d.DayOfWeek == dayOfWeek).EndTime;


                // check to see if an event spans entire work day, if it does continue loop
                var eventSpansWholeDay = events.FirstOrDefault(e => e.Start.DateTime <= workDayStartTime && e.End.DateTime >= workDayEndTime);

                if (eventSpansWholeDay != null)
                {
                    continue;
                }


                // filter events based on the current work day
                var filteredEvents = events.Where(e => e.End.DateTime >= workDayStartTime && e.Start.DateTime < workDayEndTime);
                var firstEventStart = filteredEvents.FirstOrDefault()?.Start.DateTime;
                var firstEventEnd = filteredEvents.FirstOrDefault()?.End.DateTime;


                // if the filtered events has none then the day is free
                if (!filteredEvents.Any())
                {
                    freeTimeSlots.Add(new TimePeriod
                    {
                        Start = workDayStartTime,
                        End = workDayEndTime
                    });
                }


                //if all day event continue to next lot of events
                if (allDayEvents.Any(d => d.Start.DateTime.Value.ToShortDateString() == firstEventStart?.ToShortDateString()))
                {
                    continue;
                };


                // add first slot if first event time is greater than start time
                if (firstEventStart > workDayStartTime && firstEventStart > firstDate)
                {
                    freeTimeSlots.Add(new TimePeriod
                    {
                        Start = workDayStartTime,
                        End = firstEventStart
                    });
                }


                // add the rest of the slots
                int x = 1;

                foreach (var calEvent in filteredEvents)
                {
                    var eventStart = calEvent.Start.DateTime;
                    var eventEnd = calEvent.End.DateTime;


                    var nextEventStart = filteredEvents.Skip(x).FirstOrDefault()?.Start.DateTime;
                    var nextEventEnd = filteredEvents.Skip(x).FirstOrDefault()?.End.DateTime;

                    //check to see if any events are nested inside another one
                    var eventIsNested = filteredEvents.FirstOrDefault(t => t.Start.DateTime < eventStart && t.End.DateTime > eventEnd);

                    if (eventIsNested != null)
                    {
                        eventEnd = eventIsNested.End.DateTime;
                    }

                    if (eventEnd != nextEventStart && eventEnd < nextEventStart)
                    {
                        freeTimeSlots.Add(new TimePeriod
                        {
                            Start = eventEnd,
                            End = nextEventStart > workDayEndTime ? workDayEndTime : nextEventStart
                        });
                    }


                    // must be the last event
                    if (nextEventEnd == null && eventEnd < workDayEndTime)
                    {

                        freeTimeSlots.Add(new TimePeriod
                        {
                            Start = eventEnd,
                            End = workDayEndTime
                        });
                    }

                    x++;
                }
            }

            //remove all day events
            foreach (var allDayEvent in allDayEvents)
            {

                var eventStart = allDayEvent.Start.DateTime.Value;
                var eventEnd = allDayEvent.End.DateTime.Value;

                while (eventStart < eventEnd)
                {
                    freeTimeSlots.RemoveAll(s => s.Start.Value.ToShortDateString() == eventStart.ToShortDateString());
                    eventStart = eventStart.AddDays(1);
                }

            }

            return freeTimeSlots.OrderBy(d => d.Start).ToList();
        }
    }
}
