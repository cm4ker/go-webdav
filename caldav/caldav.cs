using System;
using System.Collections.Generic;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using WebDav;

namespace CalDav
{
    public static class Capability
    {
        public static readonly string Calendar = "calendar-access";
    }

    public static class CalendarHomeSet
    {
        public static BackendSuppliedHomeSet New(string path)
        {
            return new CalendarHomeSetImpl { Href = new Uri(path) };
        }

        private class CalendarHomeSetImpl : BackendSuppliedHomeSet
        {
            public Uri Href { get; set; }
        }
    }

    public static class CalendarObjectValidator
    {
        public static (string eventType, string uid, Exception error) Validate(Calendar calendar)
        {
            string eventType = null;
            string uid = null;

            foreach (var comp in calendar.Children)
            {
                if (comp.Name == "VTIMEZONE")
                {
                    continue;
                }

                if (eventType == null)
                {
                    eventType = comp.Name;
                }
                else if (eventType != comp.Name)
                {
                    return (null, null, new Exception($"Conflicting event types in calendar: {eventType}, {comp.Name}"));
                }

                var compUID = comp.Properties.Get<string>("UID");
                if (uid == null)
                {
                    uid = compUID;
                }
                else if (uid != compUID)
                {
                    return (null, null, new Exception($"Conflicting UID values in calendar: {uid}, {compUID}"));
                }
            }

            return (eventType, uid, null);
        }
    }

    public class Calendar
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public long MaxResourceSize { get; set; }
        public List<string> SupportedComponentSet { get; set; }
    }

    public class CalendarCompRequest
    {
        public string Name { get; set; }
        public bool AllProps { get; set; }
        public List<string> Props { get; set; }
        public bool AllComps { get; set; }
        public List<CalendarCompRequest> Comps { get; set; }
    }

    public class CompFilter
    {
        public string Name { get; set; }
        public bool IsNotDefined { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public List<PropFilter> Props { get; set; }
        public List<CompFilter> Comps { get; set; }
    }

    public class ParamFilter
    {
        public string Name { get; set; }
        public bool IsNotDefined { get; set; }
        public TextMatch TextMatch { get; set; }
    }

    public class PropFilter
    {
        public string Name { get; set; }
        public bool IsNotDefined { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public TextMatch TextMatch { get; set; }
        public List<ParamFilter> ParamFilter { get; set; }
    }

    public class TextMatch
    {
        public string Text { get; set; }
        public bool NegateCondition { get; set; }
    }

    public class CalendarQuery
    {
        public CalendarCompRequest CompRequest { get; set; }
        public CompFilter CompFilter { get; set; }
    }

    public class CalendarMultiGet
    {
        public List<string> Paths { get; set; }
        public CalendarCompRequest CompRequest { get; set; }
    }

    public class CalendarObject
    {
        public string Path { get; set; }
        public DateTime ModTime { get; set; }
        public long ContentLength { get; set; }
        public string ETag { get; set; }
        public Calendar Data { get; set; }
    }
}
