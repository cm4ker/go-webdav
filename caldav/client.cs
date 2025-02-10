using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Xml.Linq;
using Ical.Net;
using Ical.Net.Serialization;
using WebDav;

namespace CalDav
{
    public class Client
    {
        private readonly WebDavClient _webDavClient;

        public Client(HttpClient httpClient, string endpoint)
        {
            _webDavClient = new WebDavClient(httpClient, endpoint);
        }

        public async Task<string> FindCalendarHomeSetAsync(string principal)
        {
            var propfind = new PropFind
            {
                Names = new List<XName> { XName.Get("calendar-home-set", "urn:ietf:params:xml:ns:caldav") }
            };

            var response = await _webDavClient.PropFindAsync(principal, propfind);
            var prop = response.GetProperty("calendar-home-set", "urn:ietf:params:xml:ns:caldav");

            return prop?.Value;
        }

        public async Task<List<Calendar>> FindCalendarsAsync(string calendarHomeSet)
        {
            var propfind = new PropFind
            {
                Names = new List<XName>
                {
                    XName.Get("resourcetype", "DAV:"),
                    XName.Get("displayname", "DAV:"),
                    XName.Get("calendar-description", "urn:ietf:params:xml:ns:caldav"),
                    XName.Get("max-resource-size", "urn:ietf:params:xml:ns:caldav"),
                    XName.Get("supported-calendar-component-set", "urn:ietf:params:xml:ns:caldav")
                }
            };

            var response = await _webDavClient.PropFindAsync(calendarHomeSet, propfind);
            var calendars = new List<Calendar>();

            foreach (var resp in response.Responses)
            {
                var path = resp.Href;
                var resType = resp.GetProperty("resourcetype", "DAV:");
                if (resType == null || !resType.Value.Contains("calendar"))
                {
                    continue;
                }

                var desc = resp.GetProperty("calendar-description", "urn:ietf:params:xml:ns:caldav");
                var dispName = resp.GetProperty("displayname", "DAV:");
                var maxResSize = resp.GetProperty("max-resource-size", "urn:ietf:params:xml:ns:caldav");
                var supportedCompSet = resp.GetProperty("supported-calendar-component-set", "urn:ietf:params:xml:ns:caldav");

                var compNames = new List<string>();
                if (supportedCompSet != null)
                {
                    foreach (var comp in supportedCompSet.Elements())
                    {
                        compNames.Add(comp.Name.LocalName);
                    }
                }

                calendars.Add(new Calendar
                {
                    Path = path,
                    Name = dispName?.Value,
                    Description = desc?.Value,
                    MaxResourceSize = maxResSize != null ? long.Parse(maxResSize.Value) : 0,
                    SupportedComponentSet = compNames
                });
            }

            return calendars;
        }

        public async Task<List<CalendarObject>> QueryCalendarAsync(string calendar, CalendarQuery query)
        {
            var propReq = await EncodeCalendarReqAsync(query.CompRequest);
            var calendarQuery = new CalendarQueryRequest
            {
                Prop = propReq,
                Filter = new CompFilterRequest
                {
                    CompFilter = EncodeCompFilter(query.CompFilter)
                }
            };

            var response = await _webDavClient.ReportAsync(calendar, calendarQuery);
            return await DecodeCalendarObjectListAsync(response);
        }

        public async Task<List<CalendarObject>> MultiGetCalendarAsync(string path, CalendarMultiGet multiGet)
        {
            var propReq = await EncodeCalendarReqAsync(multiGet.CompRequest);
            var calendarMultiget = new CalendarMultiGetRequest
            {
                Prop = propReq,
                Hrefs = multiGet.Paths.Count == 0 ? new List<string> { path } : multiGet.Paths
            };

            var response = await _webDavClient.ReportAsync(path, calendarMultiget);
            return await DecodeCalendarObjectListAsync(response);
        }

        public async Task<CalendarObject> GetCalendarObjectAsync(string path)
        {
            var response = await _webDavClient.GetAsync(path);
            var mediaType = response.Content.Headers.ContentType.MediaType;
            if (!string.Equals(mediaType, "text/calendar", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Expected Content-Type 'text/calendar', got '{mediaType}'");
            }

            var cal = await new CalendarSerializer().DeserializeAsync(await response.Content.ReadAsStreamAsync());
            var co = new CalendarObject
            {
                Path = response.RequestMessage.RequestUri.AbsolutePath,
                Data = cal
            };

            PopulateCalendarObject(co, response.Headers);
            return co;
        }

        public async Task<CalendarObject> PutCalendarObjectAsync(string path, Calendar cal)
        {
            var content = new StringContent(new CalendarSerializer().SerializeToString(cal));
            content.Headers.ContentType = new MediaTypeHeaderValue("text/calendar");

            var response = await _webDavClient.PutAsync(path, content);
            var co = new CalendarObject { Path = path };
            PopulateCalendarObject(co, response.Headers);
            return co;
        }

        private async Task<Prop> EncodeCalendarReqAsync(CalendarCompRequest c)
        {
            var compReq = await EncodeCalendarCompReqAsync(c);
            var calDataReq = new CalendarDataRequest { Comp = compReq };

            var getLastModReq = new XElement(XName.Get("getlastmodified", "DAV:"));
            var getETagReq = new XElement(XName.Get("getetag", "DAV:"));
            return new Prop
            {
                Elements = new List<XElement> { calDataReq.ToXElement(), getLastModReq, getETagReq }
            };
        }

        private async Task<Comp> EncodeCalendarCompReqAsync(CalendarCompRequest c)
        {
            var encoded = new Comp { Name = c.Name };

            if (c.AllProps)
            {
                encoded.Allprop = new XElement(XName.Get("allprop", "DAV:"));
            }
            foreach (var name in c.Props)
            {
                encoded.Prop.Add(new XElement(XName.Get("prop", "DAV:")) { Value = name });
            }

            if (c.AllComps)
            {
                encoded.Allcomp = new XElement(XName.Get("allcomp", "DAV:"));
            }
            foreach (var child in c.Comps)
            {
                var encodedChild = await EncodeCalendarCompReqAsync(child);
                encoded.Comp.Add(encodedChild);
            }

            return encoded;
        }

        private CompFilterRequest EncodeCompFilter(CompFilter filter)
        {
            var encoded = new CompFilterRequest { Name = filter.Name };
            if (filter.Start != DateTime.MinValue || filter.End != DateTime.MinValue)
            {
                encoded.TimeRange = new TimeRange
                {
                    Start = filter.Start,
                    End = filter.End
                };
            }
            foreach (var child in filter.Comps)
            {
                encoded.CompFilters.Add(EncodeCompFilter(child));
            }
            return encoded;
        }

        private async Task<List<CalendarObject>> DecodeCalendarObjectListAsync(MultiStatusResponse response)
        {
            var addrs = new List<CalendarObject>();
            foreach (var resp in response.Responses)
            {
                var path = resp.Href;
                var calData = resp.GetProperty("calendar-data", "urn:ietf:params:xml:ns:caldav");

                var getLastMod = resp.GetProperty("getlastmodified", "DAV:");
                var getETag = resp.GetProperty("getetag", "DAV:");
                var getContentLength = resp.GetProperty("getcontentlength", "DAV:");

                var data = await new CalendarSerializer().DeserializeAsync(new MemoryStream(Convert.FromBase64String(calData.Value)));

                addrs.Add(new CalendarObject
                {
                    Path = path,
                    ModTime = getLastMod != null ? DateTime.Parse(getLastMod.Value) : DateTime.MinValue,
                    ContentLength = getContentLength != null ? long.Parse(getContentLength.Value) : 0,
                    ETag = getETag?.Value,
                    Data = data
                });
            }

            return addrs;
        }

        private void PopulateCalendarObject(CalendarObject co, HttpHeaders headers)
        {
            if (headers.Location != null)
            {
                co.Path = headers.Location.AbsolutePath;
            }
            if (headers.ETag != null)
            {
                co.ETag = headers.ETag.Tag;
            }
            if (headers.ContentLength.HasValue)
            {
                co.ContentLength = headers.ContentLength.Value;
            }
            if (headers.LastModified.HasValue)
            {
                co.ModTime = headers.LastModified.Value.UtcDateTime;
            }
        }
    }
}
