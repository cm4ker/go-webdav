using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace CardDav
{
    public static class XmlNames
    {
        public static readonly XmlQualifiedName AddressBookHomeSetName = new XmlQualifiedName("addressbook-home-set", "urn:ietf:params:xml:ns:carddav");
        public static readonly XmlQualifiedName AddressBookName = new XmlQualifiedName("addressbook", "urn:ietf:params:xml:ns:carddav");
        public static readonly XmlQualifiedName AddressBookDescriptionName = new XmlQualifiedName("addressbook-description", "urn:ietf:params:xml:ns:carddav");
        public static readonly XmlQualifiedName SupportedAddressDataName = new XmlQualifiedName("supported-address-data", "urn:ietf:params:xml:ns:carddav");
        public static readonly XmlQualifiedName MaxResourceSizeName = new XmlQualifiedName("max-resource-size", "urn:ietf:params:xml:ns:carddav");
        public static readonly XmlQualifiedName AddressBookQueryName = new XmlQualifiedName("addressbook-query", "urn:ietf:params:xml:ns:carddav");
        public static readonly XmlQualifiedName AddressBookMultigetName = new XmlQualifiedName("addressbook-multiget", "urn:ietf:params:xml:ns:carddav");
        public static readonly XmlQualifiedName AddressDataName = new XmlQualifiedName("address-data", "urn:ietf:params:xml:ns:carddav");
    }

    [XmlRoot("addressbook-home-set", Namespace = "urn:ietf:params:xml:ns:carddav")]
    public class AddressBookHomeSet
    {
        [XmlElement("href", Namespace = "DAV:")]
        public string Href { get; set; }

        public XmlQualifiedName GetXmlName()
        {
            return XmlNames.AddressBookHomeSetName;
        }
    }

    [XmlRoot("addressbook-description", Namespace = "urn:ietf:params:xml:ns:carddav")]
    public class AddressBookDescription
    {
        [XmlText]
        public string Description { get; set; }
    }

    [XmlRoot("supported-address-data", Namespace = "urn:ietf:params:xml:ns:carddav")]
    public class SupportedAddressData
    {
        [XmlElement("address-data-type")]
        public List<AddressDataType> Types { get; set; }
    }

    [XmlRoot("address-data-type", Namespace = "urn:ietf:params:xml:ns:carddav")]
    public class AddressDataType
    {
        [XmlAttribute("content-type")]
        public string ContentType { get; set; }

        [XmlAttribute("version")]
        public string Version { get; set; }
    }

    [XmlRoot("max-resource-size", Namespace = "urn:ietf:params:xml:ns:carddav")]
    public class MaxResourceSize
    {
        [XmlText]
        public long Size { get; set; }
    }

    [XmlRoot("addressbook-query", Namespace = "urn:ietf:params:xml:ns:carddav")]
    public class AddressBookQuery
    {
        [XmlElement("prop", Namespace = "DAV:")]
        public Prop Prop { get; set; }

        [XmlElement("allprop", Namespace = "DAV:")]
        public object AllProp { get; set; }

        [XmlElement("propname", Namespace = "DAV:")]
        public object PropName { get; set; }

        [XmlElement("filter")]
        public Filter Filter { get; set; }

        [XmlElement("limit")]
        public Limit Limit { get; set; }
    }

    [XmlRoot("filter", Namespace = "urn:ietf:params:xml:ns:carddav")]
    public class Filter
    {
        [XmlAttribute("test")]
        public FilterTest Test { get; set; }

        [XmlElement("prop-filter")]
        public List<PropFilter> PropFilters { get; set; }
    }

    public enum FilterTest
    {
        [XmlEnum("anyof")]
        AnyOf,

        [XmlEnum("allof")]
        AllOf
    }

    [XmlRoot("prop-filter", Namespace = "urn:ietf:params:xml:ns:carddav")]
    public class PropFilter
    {
        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlAttribute("test")]
        public FilterTest Test { get; set; }

        [XmlElement("is-not-defined")]
        public object IsNotDefined { get; set; }

        [XmlElement("text-match")]
        public List<TextMatch> TextMatches { get; set; }

        [XmlElement("param-filter")]
        public List<ParamFilter> Params { get; set; }
    }

    [XmlRoot("text-match", Namespace = "urn:ietf:params:xml:ns:carddav")]
    public class TextMatch
    {
        [XmlText]
        public string Text { get; set; }

        [XmlAttribute("collation")]
        public string Collation { get; set; }

        [XmlAttribute("negate-condition")]
        public NegateCondition NegateCondition { get; set; }

        [XmlAttribute("match-type")]
        public MatchType MatchType { get; set; }
    }

    public enum NegateCondition
    {
        [XmlEnum("yes")]
        Yes,

        [XmlEnum("no")]
        No
    }

    public enum MatchType
    {
        [XmlEnum("equals")]
        Equals,

        [XmlEnum("contains")]
        Contains,

        [XmlEnum("starts-with")]
        StartsWith,

        [XmlEnum("ends-with")]
        EndsWith
    }

    [XmlRoot("param-filter", Namespace = "urn:ietf:params:xml:ns:carddav")]
    public class ParamFilter
    {
        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlElement("is-not-defined")]
        public object IsNotDefined { get; set; }

        [XmlElement("text-match")]
        public TextMatch TextMatch { get; set; }
    }

    [XmlRoot("limit", Namespace = "urn:ietf:params:xml:ns:carddav")]
    public class Limit
    {
        [XmlElement("nresults")]
        public uint NResults { get; set; }
    }

    [XmlRoot("addressbook-multiget", Namespace = "urn:ietf:params:xml:ns:carddav")]
    public class AddressBookMultiget
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

    [XmlRoot("address-data", Namespace = "urn:ietf:params:xml:ns:carddav")]
    public class AddressDataReq
    {
        [XmlElement("prop")]
        public List<Prop> Props { get; set; }

        [XmlElement("allprop")]
        public object AllProp { get; set; }
    }

    [XmlRoot("prop", Namespace = "urn:ietf:params:xml:ns:carddav")]
    public class Prop
    {
        [XmlAttribute("name")]
        public string Name { get; set; }
    }

    [XmlRoot("address-data", Namespace = "urn:ietf:params:xml:ns:carddav")]
    public class AddressDataResp
    {
        [XmlText]
        public byte[] Data { get; set; }
    }

    public class ReportReq
    {
        [XmlElement("addressbook-query")]
        public AddressBookQuery Query { get; set; }

        [XmlElement("addressbook-multiget")]
        public AddressBookMultiget Multiget { get; set; }
    }

    public class MkcolReq
    {
        [XmlElement("resourcetype", Namespace = "DAV:")]
        public ResourceType ResourceType { get; set; }

        [XmlElement("displayname", Namespace = "DAV:")]
        public string DisplayName { get; set; }

        [XmlElement("addressbook-description")]
        public AddressBookDescription Description { get; set; }
    }
}
