using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace Internal
{
    public static class Constants
    {
        public const string Namespace = "DAV:";
    }

    public class Status
    {
        [XmlIgnore]
        public int Code { get; set; }

        [XmlIgnore]
        public string Text { get; set; }

        public string MarshalText()
        {
            var text = Text;
            if (string.IsNullOrEmpty(text))
            {
                text = ((HttpStatusCode)Code).ToString();
            }
            return $"HTTP/1.1 {Code} {text}";
        }

        public void UnmarshalText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var parts = text.Split(new[] { ' ' }, 3);
            if (parts.Length != 3)
            {
                throw new FormatException($"webdav: invalid HTTP status {text}: expected 3 fields");
            }

            if (!int.TryParse(parts[1], out var code))
            {
                throw new FormatException($"webdav: invalid HTTP status {text}: failed to parse code");
            }

            Code = code;
            Text = parts[2];
        }

        public Exception Err()
        {
            if (Code != (int)HttpStatusCode.OK)
            {
                return new HttpRequestException($"HTTP Error: {Code}");
            }
            return null;
        }
    }

    public class Href
    {
        public Uri Uri { get; set; }

        public override string ToString()
        {
            return Uri.ToString();
        }

        public string MarshalText()
        {
            return ToString();
        }

        public void UnmarshalText(string text)
        {
            Uri = new Uri(text);
        }
    }

    [XmlRoot(ElementName = "multistatus", Namespace = Constants.Namespace)]
    public class MultiStatus
    {
        [XmlElement(ElementName = "response", Namespace = Constants.Namespace)]
        public List<Response> Responses { get; set; }

        [XmlElement(ElementName = "responsedescription", Namespace = Constants.Namespace)]
        public string ResponseDescription { get; set; }

        [XmlElement(ElementName = "sync-token", Namespace = Constants.Namespace)]
        public string SyncToken { get; set; }

        public MultiStatus()
        {
            Responses = new List<Response>();
        }

        public MultiStatus(params Response[] responses)
        {
            Responses = responses.ToList();
        }
    }

    [XmlRoot(ElementName = "response", Namespace = Constants.Namespace)]
    public class Response
    {
        [XmlElement(ElementName = "href", Namespace = Constants.Namespace)]
        public List<Href> Hrefs { get; set; }

        [XmlElement(ElementName = "propstat", Namespace = Constants.Namespace)]
        public List<PropStat> PropStats { get; set; }

        [XmlElement(ElementName = "responsedescription", Namespace = Constants.Namespace)]
        public string ResponseDescription { get; set; }

        [XmlElement(ElementName = "status", Namespace = Constants.Namespace)]
        public Status Status { get; set; }

        [XmlElement(ElementName = "error", Namespace = Constants.Namespace)]
        public Error Error { get; set; }

        [XmlElement(ElementName = "location", Namespace = Constants.Namespace)]
        public Location Location { get; set; }

        public Response()
        {
            Hrefs = new List<Href>();
            PropStats = new List<PropStat>();
        }

        public Response(string path)
        {
            Hrefs = new List<Href> { new Href { Uri = new Uri(path, UriKind.Relative) } };
            Status = new Status { Code = (int)HttpStatusCode.OK };
        }

        public Response(string path, Exception error)
        {
            Hrefs = new List<Href> { new Href { Uri = new Uri(path, UriKind.Relative) } };
            Status = new Status { Code = (int)HttpStatusCode.InternalServerError };
            ResponseDescription = error.Message;
            Error = new Error { Message = error.Message };
        }

        public Exception Err()
        {
            if (Status == null || Status.Code / 100 == 2)
            {
                return null;
            }

            var err = Error;
            if (!string.IsNullOrEmpty(ResponseDescription))
            {
                if (err != null)
                {
                    err = new Error { Message = $"{ResponseDescription} ({err.Message})" };
                }
                else
                {
                    err = new Error { Message = ResponseDescription };
                }
            }

            return new HttpRequestException($"HTTP Error: {Status.Code}", err);
        }

        public string Path()
        {
            var err = Err();
            if (Hrefs.Count == 1)
            {
                return Hrefs[0].Uri.ToString();
            }
            else if (err == null)
            {
                throw new InvalidOperationException($"webdav: malformed response: expected exactly one href element, got {Hrefs.Count}");
            }
            return null;
        }

        public void DecodeProp(params object[] values)
        {
            foreach (var v in values)
            {
                var name = v.GetType().Name;
                if (Err() != null)
                {
                    throw new InvalidOperationException($"property <{name}>: {Err().Message}");
                }
                foreach (var propstat in PropStats)
                {
                    var raw = propstat.Prop.Get(name);
                    if (raw == null)
                    {
                        continue;
                    }
                    if (propstat.Status.Err() != null)
                    {
                        throw new InvalidOperationException($"property <{name}>: {propstat.Status.Err().Message}");
                    }
                    raw.Decode(v);
                    return;
                }
                throw new InvalidOperationException($"property <{name}>: missing property");
            }
        }

        public void EncodeProp(int code, object v)
        {
            var raw = new RawXMLValue(v);
            foreach (var propstat in PropStats)
            {
                if (propstat.Status.Code == code)
                {
                    propstat.Prop.Raw.Add(raw);
                    return;
                }
            }

            PropStats.Add(new PropStat
            {
                Status = new Status { Code = code },
                Prop = new Prop { Raw = new List<RawXMLValue> { raw } }
            });
        }
    }

    [XmlRoot(ElementName = "location", Namespace = Constants.Namespace)]
    public class Location
    {
        [XmlElement(ElementName = "href", Namespace = Constants.Namespace)]
        public Href Href { get; set; }
    }

    [XmlRoot(ElementName = "propstat", Namespace = Constants.Namespace)]
    public class PropStat
    {
        [XmlElement(ElementName = "prop", Namespace = Constants.Namespace)]
        public Prop Prop { get; set; }

        [XmlElement(ElementName = "status", Namespace = Constants.Namespace)]
        public Status Status { get; set; }

        [XmlElement(ElementName = "responsedescription", Namespace = Constants.Namespace)]
        public string ResponseDescription { get; set; }

        [XmlElement(ElementName = "error", Namespace = Constants.Namespace)]
        public Error Error { get; set; }
    }

    [XmlRoot(ElementName = "prop", Namespace = Constants.Namespace)]
    public class Prop
    {
        [XmlAnyElement]
        public List<RawXMLValue> Raw { get; set; }

        public Prop()
        {
            Raw = new List<RawXMLValue>();
        }

        public RawXMLValue Get(string name)
        {
            return Raw.FirstOrDefault(r => r.Name == name);
        }

        public void Decode(object v)
        {
            var name = v.GetType().Name;
            var raw = Get(name);
            if (raw == null)
            {
                throw new InvalidOperationException($"missing property {name}");
            }
            raw.Decode(v);
        }
    }

    [XmlRoot(ElementName = "propfind", Namespace = Constants.Namespace)]
    public class PropFind
    {
        [XmlElement(ElementName = "prop", Namespace = Constants.Namespace)]
        public Prop Prop { get; set; }

        [XmlElement(ElementName = "allprop", Namespace = Constants.Namespace)]
        public object AllProp { get; set; }

        [XmlElement(ElementName = "include", Namespace = Constants.Namespace)]
        public Include Include { get; set; }

        [XmlElement(ElementName = "propname", Namespace = Constants.Namespace)]
        public object PropName { get; set; }

        public PropFind()
        {
        }

        public PropFind(params string[] names)
        {
            Prop = new Prop { Raw = names.Select(n => new RawXMLValue(n)).ToList() };
        }
    }

    [XmlRoot(ElementName = "include", Namespace = Constants.Namespace)]
    public class Include
    {
        [XmlAnyElement]
        public List<RawXMLValue> Raw { get; set; }

        public Include()
        {
            Raw = new List<RawXMLValue>();
        }
    }

    [XmlRoot(ElementName = "resourcetype", Namespace = Constants.Namespace)]
    public class ResourceType
    {
        [XmlAnyElement]
        public List<RawXMLValue> Raw { get; set; }

        public ResourceType()
        {
            Raw = new List<RawXMLValue>();
        }

        public ResourceType(params string[] names)
        {
            Raw = names.Select(n => new RawXMLValue(n)).ToList();
        }

        public bool Is(string name)
        {
            return Raw.Any(r => r.Name == name);
        }
    }

    [XmlRoot(ElementName = "getcontentlength", Namespace = Constants.Namespace)]
    public class GetContentLength
    {
        [XmlText]
        public long Length { get; set; }
    }

    [XmlRoot(ElementName = "getcontenttype", Namespace = Constants.Namespace)]
    public class GetContentType
    {
        [XmlText]
        public string Type { get; set; }
    }

    public class Time
    {
        public DateTime DateTime { get; set; }

        public void UnmarshalText(string text)
        {
            DateTime = DateTime.Parse(text);
        }

        public string MarshalText()
        {
            return DateTime.ToString("r");
        }
    }

    [XmlRoot(ElementName = "getlastmodified", Namespace = Constants.Namespace)]
    public class GetLastModified
    {
        [XmlElement(ElementName = "getlastmodified", Namespace = Constants.Namespace)]
        public Time LastModified { get; set; }
    }

    [XmlRoot(ElementName = "getetag", Namespace = Constants.Namespace)]
    public class GetETag
    {
        [XmlText]
        public string ETag { get; set; }

        public void UnmarshalText(string text)
        {
            ETag = text.Trim('"');
        }

        public string MarshalText()
        {
            return $"\"{ETag}\"";
        }
    }

    [XmlRoot(ElementName = "error", Namespace = Constants.Namespace)]
    public class Error : Exception
    {
        [XmlAnyElement]
        public List<RawXMLValue> Raw { get; set; }

        [XmlIgnore]
        public string Message { get; set; }

        public Error()
        {
            Raw = new List<RawXMLValue>();
        }

        public override string ToString()
        {
            var xmlSerializer = new XmlSerializer(typeof(Error));
            using (var stringWriter = new StringWriter())
            {
                xmlSerializer.Serialize(stringWriter, this);
                return stringWriter.ToString();
            }
        }
    }

    [XmlRoot(ElementName = "displayname", Namespace = Constants.Namespace)]
    public class DisplayName
    {
        [XmlText]
        public string Name { get; set; }
    }

    [XmlRoot(ElementName = "current-user-principal", Namespace = Constants.Namespace)]
    public class CurrentUserPrincipal
    {
        [XmlElement(ElementName = "href", Namespace = Constants.Namespace)]
        public Href Href { get; set; }

        [XmlElement(ElementName = "unauthenticated", Namespace = Constants.Namespace)]
        public object Unauthenticated { get; set; }
    }

    [XmlRoot(ElementName = "propertyupdate", Namespace = Constants.Namespace)]
    public class PropertyUpdate
    {
        [XmlElement(ElementName = "remove", Namespace = Constants.Namespace)]
        public List<Remove> Remove { get; set; }

        [XmlElement(ElementName = "set", Namespace = Constants.Namespace)]
        public List<Set> Set { get; set; }

        public PropertyUpdate()
        {
            Remove = new List<Remove>();
            Set = new List<Set>();
        }
    }

    [XmlRoot(ElementName = "remove", Namespace = Constants.Namespace)]
    public class Remove
    {
        [XmlElement(ElementName = "prop", Namespace = Constants.Namespace)]
        public Prop Prop { get; set; }
    }

    [XmlRoot(ElementName = "set", Namespace = Constants.Namespace)]
    public class Set
    {
        [XmlElement(ElementName = "prop", Namespace = Constants.Namespace)]
        public Prop Prop { get; set; }
    }

    [XmlRoot(ElementName = "sync-collection", Namespace = Constants.Namespace)]
    public class SyncCollectionQuery
    {
        [XmlElement(ElementName = "sync-token", Namespace = Constants.Namespace)]
        public string SyncToken { get; set; }

        [XmlElement(ElementName = "limit", Namespace = Constants.Namespace)]
        public Limit Limit { get; set; }

        [XmlElement(ElementName = "sync-level", Namespace = Constants.Namespace)]
        public string SyncLevel { get; set; }

        [XmlElement(ElementName = "prop", Namespace = Constants.Namespace)]
        public Prop Prop { get; set; }
    }

    [XmlRoot(ElementName = "limit", Namespace = Constants.Namespace)]
    public class Limit
    {
        [XmlElement(ElementName = "nresults", Namespace = Constants.Namespace)]
        public uint NResults { get; set; }
    }

    public class RawXMLValue
    {
        [XmlAnyElement]
        public XmlElement Element { get; set; }

        [XmlIgnore]
        public string Name => Element?.Name;

        public RawXMLValue()
        {
        }

        public RawXMLValue(object value)
        {
            var xmlSerializer = new XmlSerializer(value.GetType());
            using (var stringWriter = new StringWriter())
            {
                xmlSerializer.Serialize(stringWriter, value);
                var xmlDocument = new XmlDocument();
                xmlDocument.LoadXml(stringWriter.ToString());
                Element = xmlDocument.DocumentElement;
            }
        }

        public void Decode(object value)
        {
            var xmlSerializer = new XmlSerializer(value.GetType());
            using (var stringReader = new StringReader(Element.OuterXml))
            {
                var deserializedValue = xmlSerializer.Deserialize(stringReader);
                foreach (var property in value.GetType().GetProperties())
                {
                    property.SetValue(value, property.GetValue(deserializedValue));
                }
            }
        }
    }
}
