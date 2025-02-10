using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace CalDav
{
    public static class XmlNames
    {
        public static readonly XmlQualifiedName CalendarHomeSetName = new XmlQualifiedName("calendar-home-set", "urn:ietf:params:xml:ns:caldav");
        public static readonly XmlQualifiedName CalendarDescriptionName = new XmlQualifiedName("calendar-description", "urn:ietf:params:xml:ns:caldav");
        public static readonly XmlQualifiedName SupportedCalendarDataName = new XmlQualifiedName("supported-calendar-data", "urn:ietf:params:xml:ns:caldav");
        public static readonly XmlQualifiedName SupportedCalendarComponentSetName = new XmlQualifiedName("supported-calendar-component-set", "urn:ietf:params:xml:ns:caldav");
        public static readonly XmlQualifiedName MaxResourceSizeName = new XmlQualifiedName("max-resource-size", "urn:ietf:params:xml:ns:caldav");
        public static readonly XmlQualifiedName CalendarQueryName = new XmlQualifiedName("calendar-query", "urn:ietf:params:xml:ns:caldav");
        public static readonly XmlQualifiedName CalendarMultigetName = new XmlQualifiedName("calendar-multiget", "urn:ietf:params:xml:ns:caldav");
        public static readonly XmlQualifiedName CalendarName = new XmlQualifiedName("calendar", "urn:ietf:params:xml:ns:caldav");
        public static readonly XmlQualifiedName CalendarDataName = new XmlQualifiedName("calendar-data", "urn:ietf:params:xml:ns:caldav");
    }

    [XmlRoot("calendar-home-set", Namespace = "urn:ietf:params:xml:ns:caldav")]
    public class CalendarHomeSet
    {
        [XmlElement("href", Namespace = "DAV:")]
        public string Href { get; set; }

        public XmlQualifiedName GetXmlName()
        {
            return XmlNames.CalendarHomeSetName;
        }
    }

    [XmlRoot("calendar-description", Namespace = "urn:ietf:params:xml:ns:caldav")]
    public class CalendarDescription
    {
        [XmlText]
        public string Description { get; set; }
    }

    [XmlRoot("supported-calendar-data", Namespace = "urn:ietf:params:xml:ns:caldav")]
    public class SupportedCalendarData
    {
        [XmlElement("calendar-data")]
        public List<CalendarDataType> Types { get; set; }
    }

    [XmlRoot("supported-calendar-component-set", Namespace = "urn:ietf:params:xml:ns:caldav")]
    public class SupportedCalendarComponentSet
    {
        [XmlElement("comp")]
        public List<Comp> Comp { get; set; }
    }

    [XmlRoot("calendar-data", Namespace = "urn:ietf:params:xml:ns:caldav")]
    public class CalendarDataType
    {
        [XmlAttribute("content-type")]
        public string ContentType { get; set; }

        [XmlAttribute("version")]
        public string Version { get; set; }
    }

    [XmlRoot("max-resource-size", Namespace = "urn:ietf:params:xml:ns:caldav")]
    public class MaxResourceSize
    {
        [XmlText]
        public long Size { get; set; }
    }

    [XmlRoot("calendar-query", Namespace = "urn:ietf:params:xml:ns:caldav")]
    public class CalendarQuery
    {
        [XmlElement("prop", Namespace = "DAV:")]
        public Prop Prop { get; set; }

        [XmlElement("allprop", Namespace = "DAV:")]
        public object AllProp { get; set; }

        [XmlElement("propname", Namespace = "DAV:")]
        public object PropName { get; set; }

        [XmlElement("filter")]
        public Filter Filter { get; set; }
    }

    [XmlRoot("calendar-multiget", Namespace = "urn:ietf:params:xml:ns:caldav")]
    public class CalendarMultiget
    {
        [XmlElement("href", Namespace = "DAV:")]
        public List<string> Hrefs { get; set; }

        [XmlElement("prop", Namespace = "DAV:")]
        public Prop Prop { get; set; }

        [XmlElement("allprop", Namespace = "DAV:")]
        public object AllProp { get; set; }

        [XmlElement("propname", Namespace = "DAV:")]
        public object PropName { get; set; }
    }

    [XmlRoot("filter", Namespace = "urn:ietf:params:xml:ns:caldav")]
    public class Filter
    {
        [XmlElement("comp-filter")]
        public CompFilter CompFilter { get; set; }
    }

    [XmlRoot("comp-filter", Namespace = "urn:ietf:params:xml:ns:caldav")]
    public class CompFilter
    {
        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlElement("is-not-defined")]
        public object IsNotDefined { get; set; }

        [XmlElement("time-range")]
        public TimeRange TimeRange { get; set; }

        [XmlElement("prop-filter")]
        public List<PropFilter> PropFilters { get; set; }

        [XmlElement("comp-filter")]
        public List<CompFilter> CompFilters { get; set; }
    }

    [XmlRoot("prop-filter", Namespace = "urn:ietf:params:xml:ns:caldav")]
    public class PropFilter
    {
        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlElement("is-not-defined")]
        public object IsNotDefined { get; set; }

        [XmlElement("time-range")]
        public TimeRange TimeRange { get; set; }

        [XmlElement("text-match")]
        public TextMatch TextMatch { get; set; }

        [XmlElement("param-filter")]
        public List<ParamFilter> ParamFilter { get; set; }
    }

    [XmlRoot("param-filter", Namespace = "urn:ietf:params:xml:ns:caldav")]
    public class ParamFilter
    {
        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlElement("is-not-defined")]
        public object IsNotDefined { get; set; }

        [XmlElement("text-match")]
        public TextMatch TextMatch { get; set; }
    }

    [XmlRoot("text-match", Namespace = "urn:ietf:params:xml:ns:caldav")]
    public class TextMatch
    {
        [XmlText]
        public string Text { get; set; }

        [XmlAttribute("collation")]
        public string Collation { get; set; }

        [XmlAttribute("negate-condition")]
        public NegateCondition NegateCondition { get; set; }
    }

    public enum NegateCondition
    {
        [XmlEnum("yes")]
        Yes,

        [XmlEnum("no")]
        No
    }

    [XmlRoot("time-range", Namespace = "urn:ietf:params:xml:ns:caldav")]
    public class TimeRange
    {
        [XmlAttribute("start")]
        public DateWithUTCTime Start { get; set; }

        [XmlAttribute("end")]
        public DateWithUTCTime End { get; set; }
    }

    public class DateWithUTCTime
    {
        private const string DateWithUTCTimeLayout = "yyyyMMddTHHmmssZ";

        private DateTime _dateTime;

        public DateWithUTCTime()
        {
        }

        public DateWithUTCTime(DateTime dateTime)
        {
            _dateTime = dateTime;
        }

        public static implicit operator DateTime(DateWithUTCTime d)
        {
            return d._dateTime;
        }

        public static implicit operator DateWithUTCTime(DateTime d)
        {
            return new DateWithUTCTime(d);
        }

        public override string ToString()
        {
            return _dateTime.ToString(DateWithUTCTimeLayout);
        }

        public static DateWithUTCTime Parse(string s)
        {
            return new DateWithUTCTime(DateTime.ParseExact(s, DateWithUTCTimeLayout, null));
        }
    }

    [XmlRoot("calendar-data", Namespace = "urn:ietf:params:xml:ns:caldav")]
    public class CalendarDataReq
    {
        [XmlElement("comp")]
        public Comp Comp { get; set; }
    }

    [XmlRoot("comp", Namespace = "urn:ietf:params:xml:ns:caldav")]
    public class Comp
    {
        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlElement("allprop")]
        public object Allprop { get; set; }

        [XmlElement("prop")]
        public List<Prop> Prop { get; set; }

        [XmlElement("allcomp")]
        public object Allcomp { get; set; }

        [XmlElement("comp")]
        public List<Comp> Comp { get; set; }
    }

    [XmlRoot("prop", Namespace = "urn:ietf:params:xml:ns:caldav")]
    public class Prop
    {
        [XmlAttribute("name")]
        public string Name { get; set; }
    }

    [XmlRoot("calendar-data", Namespace = "urn:ietf:params:xml:ns:caldav")]
    public class CalendarDataResp
    {
        [XmlText]
        public byte[] Data { get; set; }
    }

    public class ReportReq
    {
        [XmlElement("calendar-query")]
        public CalendarQuery Query { get; set; }

        [XmlElement("calendar-multiget")]
        public CalendarMultiget Multiget { get; set; }
    }

    public class MkcolReq
    {
        [XmlElement("resourcetype", Namespace = "DAV:")]
        public ResourceType ResourceType { get; set; }

        [XmlElement("displayname", Namespace = "DAV:")]
        public string DisplayName { get; set; }
    }
}
