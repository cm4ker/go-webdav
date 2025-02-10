using System;
using System.Collections.Generic;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using WebDav;

namespace CardDav
{
    public static class Capability
    {
        public static readonly string AddressBook = "addressbook";
    }

    public static class AddressBookHomeSet
    {
        public static BackendSuppliedHomeSet New(string path)
        {
            return new AddressBookHomeSetImpl { Href = new Uri(path) };
        }

        private class AddressBookHomeSetImpl : BackendSuppliedHomeSet
        {
            public Uri Href { get; set; }
        }
    }

    public class AddressDataType
    {
        public string ContentType { get; set; }
        public string Version { get; set; }
    }

    public class AddressBook
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public long MaxResourceSize { get; set; }
        public List<AddressDataType> SupportedAddressData { get; set; }

        public bool SupportsAddressData(string contentType, string version)
        {
            if (SupportedAddressData == null || SupportedAddressData.Count == 0)
            {
                return contentType == "text/vcard" && version == "3.0";
            }

            foreach (var t in SupportedAddressData)
            {
                if (t.ContentType == contentType && t.Version == version)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public class AddressBookQuery
    {
        public AddressDataRequest DataRequest { get; set; }
        public List<PropFilter> PropFilters { get; set; }
        public FilterTest FilterTest { get; set; } = FilterTest.AnyOf;
        public int Limit { get; set; } = 0;
    }

    public class AddressDataRequest
    {
        public List<string> Props { get; set; }
        public bool AllProp { get; set; }
    }

    public class PropFilter
    {
        public string Name { get; set; }
        public FilterTest Test { get; set; } = FilterTest.AnyOf;
        public bool IsNotDefined { get; set; }
        public List<TextMatch> TextMatches { get; set; }
        public List<ParamFilter> Params { get; set; }
    }

    public class ParamFilter
    {
        public string Name { get; set; }
        public bool IsNotDefined { get; set; }
        public TextMatch TextMatch { get; set; }
    }

    public class TextMatch
    {
        public string Text { get; set; }
        public bool NegateCondition { get; set; }
        public MatchType MatchType { get; set; } = MatchType.Contains;
    }

    public enum FilterTest
    {
        AnyOf,
        AllOf
    }

    public enum MatchType
    {
        Equals,
        Contains,
        StartsWith,
        EndsWith
    }

    public class AddressBookMultiGet
    {
        public List<string> Paths { get; set; }
        public AddressDataRequest DataRequest { get; set; }
    }

    public class AddressObject
    {
        public string Path { get; set; }
        public DateTime ModTime { get; set; }
        public long ContentLength { get; set; }
        public string ETag { get; set; }
        public VCard Card { get; set; }
    }

    public class SyncQuery
    {
        public AddressDataRequest DataRequest { get; set; }
        public string SyncToken { get; set; }
        public int Limit { get; set; } = 0;
    }

    public class SyncResponse
    {
        public string SyncToken { get; set; }
        public List<AddressObject> Updated { get; set; }
        public List<string> Deleted { get; set; }
    }
}
