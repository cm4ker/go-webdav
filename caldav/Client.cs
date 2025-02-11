using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
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
        private readonly InternalClient _internalClient;

        public Client(HttpClient httpClient, string endpoint)
        {
            _webDavClient = new WebDavClient(httpClient, endpoint);
            _internalClient = new InternalClient(httpClient, endpoint);
        }

        public static async Task<string> DiscoverContextUrlAsync(string domain, CancellationToken cancellationToken = default)
        {
            return await InternalClient.DiscoverContextUrlAsync("caldavs", domain, cancellationToken);
        }

        public async Task<string> FindCalendarHomeSetAsync(string principal, CancellationToken cancellationToken = default)
        {
            var propfind = InternalClient.NewPropNamePropFind("calendar-home-set");
            var response = await _internalClient.PropFindFlatAsync(principal, propfind, cancellationToken);

            var prop = response.DecodeProp<CalendarHomeSet>();
            return prop.Href.Path;
        }

        public async Task<List<Calendar>> FindCalendarsAsync(string calendarHomeSet, CancellationToken cancellationToken = default)
        {
            var propfind = InternalClient.NewPropNamePropFind(
                "resourcetype",
                "displayname",
                "calendar-description",
                "max-resource-size",
                "supported-calendar-component-set"
            );
            var multiStatus = await _internalClient.PropFindAsync(calendarHomeSet, 1, propfind, cancellationToken);

            var calendars = new List<Calendar>();
            foreach (var response in multiStatus.Responses)
            {
                var path = response.Path;

                var resType = response.DecodeProp<ResourceType>();
                if (!resType.Is("calendar"))
                {
                    continue;
                }

                var desc = response.DecodeProp<CalendarDescription>();
                var dispName = response.DecodeProp<DisplayName>();
                var maxResSize = response.DecodeProp<MaxResourceSize>();
                if (maxResSize.Size < 0)
                {
                    throw new Exception("caldav: max-resource-size must be a positive integer");
                }

                var supportedCompSet = response.DecodeProp<SupportedCalendarComponentSet>();
                var compNames = supportedCompSet.Comp.Select(comp => comp.Name).ToList();

                calendars.Add(new Calendar
                {
                    Path = path,
                    Name = dispName.Name,
                    Description = desc.Description,
                    MaxResourceSize = maxResSize.Size,
                    SupportedComponentSet = compNames
                });
            }

            return calendars;
        }

        private static Comp EncodeCalendarCompReq(CalendarCompRequest request)
        {
            var comp = new Comp { Name = request.Name };

            if (request.AllProps)
            {
                comp.Allprop = new object();
            }
            comp.Prop = request.Props.Select(name => new Prop { Name = name }).ToList();

            if (request.AllComps)
            {
                comp.Allcomp = new object();
            }
            comp.Comp = request.Comps.Select(EncodeCalendarCompReq).ToList();

            return comp;
        }

        private static Prop EncodeCalendarReq(CalendarCompRequest request)
        {
            var compReq = EncodeCalendarCompReq(request);
            var calDataReq = new CalendarDataReq { Comp = compReq };

            var getLastModReq = new RawXMLElement("getlastmodified", null, null);
            var getETagReq = new RawXMLElement("getetag", null, null);
            return InternalClient.EncodeProp(calDataReq, getLastModReq, getETagReq);
        }

        private static CompFilter EncodeCompFilter(CompFilter filter)
        {
            var encoded = new CompFilter { Name = filter.Name };
            if (filter.Start != DateTime.MinValue || filter.End != DateTime.MinValue)
            {
                encoded.TimeRange = new TimeRange
                {
                    Start = filter.Start,
                    End = filter.End
                };
            }
            encoded.CompFilters = filter.Comps.Select(EncodeCompFilter).ToList();
            return encoded;
        }

        private static async Task<List<CalendarObject>> DecodeCalendarObjectList(MultiStatus multiStatus)
        {
            var calendarObjects = new List<CalendarObject>();
            foreach (var response in multiStatus.Responses)
            {
                var path = response.Path;

                var calData = response.DecodeProp<CalendarDataResp>();
                var getLastMod = response.DecodeProp<GetLastModified>();
                var getETag = response.DecodeProp<GetETag>();
                var getContentLength = response.DecodeProp<GetContentLength>();

                var data = Calendar.Load(new StringReader(calData.Data));

                calendarObjects.Add(new CalendarObject
                {
                    Path = path,
                    ModTime = getLastMod.LastModified,
                    ContentLength = getContentLength.Length,
                    ETag = getETag.ETag,
                    Data = data
                });
            }

            return calendarObjects;
        }

        public async Task<List<CalendarObject>> QueryCalendarAsync(string calendar, CalendarQuery query, CancellationToken cancellationToken = default)
        {
            var propReq = EncodeCalendarReq(query.CompRequest);

            var calendarQuery = new CalendarQuery
            {
                Prop = propReq,
                Filter = new CompFilter
                {
                    CompFilter = EncodeCompFilter(query.CompFilter)
                }
            };
            var request = await _internalClient.NewXmlRequestAsync("REPORT", calendar, calendarQuery, cancellationToken);
            request.Headers.Add("Depth", "1");

            var multiStatus = await _internalClient.DoMultiStatusAsync(request, cancellationToken);
            return await DecodeCalendarObjectList(multiStatus);
        }

        public async Task<List<CalendarObject>> MultiGetCalendarAsync(string path, CalendarMultiGet multiGet, CancellationToken cancellationToken = default)
        {
            var propReq = EncodeCalendarReq(multiGet.CompRequest);

            var calendarMultiget = new CalendarMultiGet
            {
                Prop = propReq,
                Hrefs = multiGet.Paths.Any() ? multiGet.Paths.Select(p => new Href { Path = p }).ToList() : new List<Href> { new Href { Path = path } }
            };

            var request = await _internalClient.NewXmlRequestAsync("REPORT", path, calendarMultiget, cancellationToken);
            request.Headers.Add("Depth", "1");

            var multiStatus = await _internalClient.DoMultiStatusAsync(request, cancellationToken);
            return await DecodeCalendarObjectList(multiStatus);
        }

        private static void PopulateCalendarObject(CalendarObject calendarObject, HttpResponseHeaders headers)
        {
            if (headers.Location != null)
            {
                calendarObject.Path = headers.Location.AbsolutePath;
            }
            if (headers.ETag != null)
            {
                calendarObject.ETag = headers.ETag.Tag.Trim('"');
            }
            if (headers.ContentLength.HasValue)
            {
                calendarObject.ContentLength = headers.ContentLength.Value;
            }
            if (headers.LastModified.HasValue)
            {
                calendarObject.ModTime = headers.LastModified.Value.UtcDateTime;
            }
        }

        public async Task<CalendarObject> GetCalendarObjectAsync(string path, CancellationToken cancellationToken = default)
        {
            var request = await _internalClient.NewRequestAsync(HttpMethod.Get, path, cancellationToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/calendar"));

            var response = await _internalClient.DoAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var mediaType = response.Content.Headers.ContentType.MediaType;
            if (!string.Equals(mediaType, "text/calendar", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"caldav: expected Content-Type 'text/calendar', got '{mediaType}'");
            }

            var data = Calendar.Load(await response.Content.ReadAsStreamAsync());

            var calendarObject = new CalendarObject
            {
                Path = response.RequestMessage.RequestUri.AbsolutePath,
                Data = data
            };
            PopulateCalendarObject(calendarObject, response.Headers);
            return calendarObject;
        }

        public async Task<CalendarObject> PutCalendarObjectAsync(string path, Calendar calendar, CancellationToken cancellationToken = default)
        {
            var serializer = new CalendarSerializer();
            var serializedCalendar = serializer.SerializeToString(calendar);

            var content = new StringContent(serializedCalendar);
            content.Headers.ContentType = new MediaTypeHeaderValue("text/calendar");

            var request = await _internalClient.NewRequestAsync(HttpMethod.Put, path, content, cancellationToken);

            var response = await _internalClient.DoAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var calendarObject = new CalendarObject { Path = path };
            PopulateCalendarObject(calendarObject, response.Headers);
            return calendarObject;
        }
    }
}
