using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using WebDav.Internal;

namespace WebDav
{
    public static class XmlNames
    {
        public static readonly XmlQualifiedName PrincipalName = new XmlQualifiedName("principal", "DAV:");
        public static readonly XmlQualifiedName PrincipalAlternateURISetName = new XmlQualifiedName("alternate-URI-set", "DAV:");
        public static readonly XmlQualifiedName PrincipalURLName = new XmlQualifiedName("principal-URL", "DAV:");
        public static readonly XmlQualifiedName GroupMembershipName = new XmlQualifiedName("group-membership", "DAV:");
    }

    [XmlRoot("alternate-URI-set", Namespace = "DAV:")]
    public class PrincipalAlternateURISet
    {
        [XmlElement("href", Namespace = "DAV:")]
        public List<Href> Hrefs { get; set; }

        public XmlQualifiedName GetXmlName()
        {
            return XmlNames.PrincipalAlternateURISetName;
        }
    }

    [XmlRoot("principal-URL", Namespace = "DAV:")]
    public class PrincipalURL
    {
        [XmlElement("href", Namespace = "DAV:")]
        public Href Href { get; set; }
    }

    [XmlRoot("group-membership", Namespace = "DAV:")]
    public class GroupMembership
    {
        [XmlElement("href", Namespace = "DAV:")]
        public List<Href> Hrefs { get; set; }
    }
}
