using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Ical.Net;
using Ical.Net.Serialization;
using WebDav;

namespace CalDav
{
    public class PutCalendarObjectOptions
    {
        public ConditionalMatch IfNoneMatch { get; set; }
        public ConditionalMatch IfMatch { get; set; }
    }

    public interface Backend
    {
        string CalendarHomeSetPath();
        void CreateCalendar(Calendar calendar);
        List<Calendar> ListCalendars();
        Calendar GetCalendar(string path);
        CalendarObject GetCalendarObject(string path, CalendarCompRequest req);
        List<CalendarObject> ListCalendarObjects(string path, CalendarCompRequest req);
        List<CalendarObject> QueryCalendarObjects(string path, CalendarQuery query);
        CalendarObject PutCalendarObject(string path, Calendar calendar, PutCalendarObjectOptions opts);
        void DeleteCalendarObject(string path);
        string CurrentUserPrincipal();
    }

    public class Handler : IHttpHandler
    {
        public Backend Backend { get; set; }
        public string Prefix { get; set; }

        public void ProcessRequest(HttpContext context)
        {
            if (Backend == null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.Write("caldav: no backend available");
                return;
            }

            if (context.Request.Path == "/.well-known/caldav")
            {
                var principalPath = Backend.CurrentUserPrincipal();
                context.Response.Redirect(principalPath, true);
                return;
            }

            try
            {
                switch (context.Request.HttpMethod)
                {
                    case "REPORT":
                        HandleReport(context);
                        break;
                    default:
                        var b = new BackendWrapper
                        {
                            Backend = Backend,
                            Prefix = Prefix.TrimEnd('/')
                        };
                        var hh = new WebDavHandler { Backend = b };
                        hh.ProcessRequest(context);
                        break;
                }
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.Write(ex.Message);
            }
        }

        private void HandleReport(HttpContext context)
        {
            var report = Deserialize<ReportReq>(context.Request.InputStream);
            if (report.Query != null)
            {
                HandleQuery(context, report.Query);
            }
            else if (report.Multiget != null)
            {
                HandleMultiget(context, report.Multiget);
            }
            else
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.Write("caldav: expected calendar-query or calendar-multiget element in REPORT request");
            }
        }

        private void HandleQuery(HttpContext context, CalendarQuery query)
        {
            var q = new CalendarQuery();
            q.CompFilter = DecodeCompFilter(query.Filter.CompFilter);

            var cos = Backend.QueryCalendarObjects(context.Request.Path, q);

            var resps = new List<Response>();
            foreach (var co in cos)
            {
                var b = new BackendWrapper
                {
                    Backend = Backend,
                    Prefix = Prefix.TrimEnd('/')
                };
                var propfind = new PropFind
                {
                    Prop = query.Prop,
                    AllProp = query.AllProp,
                    PropName = query.PropName
                };
                var resp = b.PropFindCalendarObject(context, propfind, co);
                resps.Add(resp);
            }

            var ms = new MultiStatus(resps.ToArray());
            context.Response.ContentType = "application/xml";
            context.Response.Write(Serialize(ms));
        }

        private void HandleMultiget(HttpContext context, CalendarMultiget multiget)
        {
            var dataReq = new CalendarCompRequest();
            if (multiget.Prop != null)
            {
                var calendarData = Deserialize<CalendarDataReq>(multiget.Prop);
                dataReq = DecodeCalendarDataReq(calendarData);
            }

            var resps = new List<Response>();
            foreach (var href in multiget.Hrefs)
            {
                var co = Backend.GetCalendarObject(href, dataReq);
                var b = new BackendWrapper
                {
                    Backend = Backend,
                    Prefix = Prefix.TrimEnd('/')
                };
                var propfind = new PropFind
                {
                    Prop = multiget.Prop,
                    AllProp = multiget.AllProp,
                    PropName = multiget.PropName
                };
                var resp = b.PropFindCalendarObject(context, propfind, co);
                resps.Add(resp);
            }

            var ms = new MultiStatus(resps.ToArray());
            context.Response.ContentType = "application/xml";
            context.Response.Write(Serialize(ms));
        }

        private T Deserialize<T>(Stream stream)
        {
            var serializer = new XmlSerializer(typeof(T));
            return (T)serializer.Deserialize(stream);
        }

        private string Serialize<T>(T obj)
        {
            var serializer = new XmlSerializer(typeof(T));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, obj);
                return writer.ToString();
            }
        }

        private CompFilter DecodeCompFilter(CompFilter filter)
        {
            var cf = new CompFilter { Name = filter.Name };
            if (filter.IsNotDefined)
            {
                cf.IsNotDefined = true;
            }
            if (filter.Start != DateTime.MinValue)
            {
                cf.Start = filter.Start;
            }
            if (filter.End != DateTime.MinValue)
            {
                cf.End = filter.End;
            }
            foreach (var pf in filter.Props)
            {
                cf.Props.Add(DecodePropFilter(pf));
            }
            foreach (var child in filter.Comps)
            {
                cf.Comps.Add(DecodeCompFilter(child));
            }
            return cf;
        }

        private PropFilter DecodePropFilter(PropFilter filter)
        {
            var pf = new PropFilter { Name = filter.Name };
            if (filter.IsNotDefined)
            {
                pf.IsNotDefined = true;
            }
            if (filter.Start != DateTime.MinValue)
            {
                pf.Start = filter.Start;
            }
            if (filter.End != DateTime.MinValue)
            {
                pf.End = filter.End;
            }
            if (filter.TextMatch != null)
            {
                pf.TextMatch = new TextMatch { Text = filter.TextMatch.Text };
            }
            foreach (var param in filter.ParamFilter)
            {
                pf.ParamFilter.Add(DecodeParamFilter(param));
            }
            return pf;
        }

        private ParamFilter DecodeParamFilter(ParamFilter filter)
        {
            var pf = new ParamFilter { Name = filter.Name };
            if (filter.IsNotDefined)
            {
                pf.IsNotDefined = true;
            }
            if (filter.TextMatch != null)
            {
                pf.TextMatch = new TextMatch { Text = filter.TextMatch.Text };
            }
            return pf;
        }

        private CalendarCompRequest DecodeCalendarDataReq(CalendarDataReq calendarData)
        {
            if (calendarData.Comp == null)
            {
                return new CalendarCompRequest
                {
                    AllProps = true,
                    AllComps = true
                };
            }
            return DecodeComp(calendarData.Comp);
        }

        private CalendarCompRequest DecodeComp(Comp comp)
        {
            if (comp == null)
            {
                throw new InvalidOperationException("caldav: unexpected empty calendar-data in request");
            }
            if (comp.Allprop != null && comp.Prop.Count > 0)
            {
                throw new InvalidOperationException("caldav: only one of allprop or prop can be specified in calendar-data");
            }
            if (comp.Allcomp != null && comp.Comp.Count > 0)
            {
                throw new InvalidOperationException("caldav: only one of allcomp or comp can be specified in calendar-data");
            }

            var req = new CalendarCompRequest
            {
                AllProps = comp.Allprop != null,
                AllComps = comp.Allcomp != null
            };
            foreach (var p in comp.Prop)
            {
                req.Props.Add(p.Name);
            }
            foreach (var c in comp.Comp)
            {
                req.Comps.Add(DecodeComp(c));
            }
            return req;
        }
    }

    public class BackendWrapper : WebDavBackend
    {
        public Backend Backend { get; set; }
        public string Prefix { get; set; }

        public override List<string> Options(HttpContext context)
        {
            var caps = new List<string> { "calendar-access" };

            if (ResourceTypeAtPath(context.Request.Path) != ResourceType.CalendarObject)
            {
                return new List<string> { "OPTIONS", "PROPFIND", "REPORT", "DELETE", "MKCOL" };
            }

            var dataReq = new CalendarCompRequest();
            try
            {
                Backend.GetCalendarObject(context.Request.Path, dataReq);
                return new List<string> { "OPTIONS", "HEAD", "GET", "PUT", "DELETE", "PROPFIND" };
            }
            catch (HttpException ex) when (ex.GetHttpCode() == (int)HttpStatusCode.NotFound)
            {
                return new List<string> { "OPTIONS", "PUT" };
            }
        }

        public override void HeadGet(HttpContext context)
        {
            var dataReq = new CalendarCompRequest();
            if (context.Request.HttpMethod != "HEAD")
            {
                dataReq.AllProps = true;
            }
            var co = Backend.GetCalendarObject(context.Request.Path, dataReq);

            context.Response.ContentType = "text/calendar";
            if (co.ContentLength > 0)
            {
                context.Response.Headers["Content-Length"] = co.ContentLength.ToString();
            }
            if (!string.IsNullOrEmpty(co.ETag))
            {
                context.Response.Headers["ETag"] = co.ETag;
            }
            if (co.ModTime != DateTime.MinValue)
            {
                context.Response.Headers["Last-Modified"] = co.ModTime.ToUniversalTime().ToString("R");
            }

            if (context.Request.HttpMethod != "HEAD")
            {
                var serializer = new CalendarSerializer();
                var calendarString = serializer.SerializeToString(co.Data);
                context.Response.Write(calendarString);
            }
        }

        public override MultiStatus PropFind(HttpContext context, PropFind propfind, Depth depth)
        {
            var resType = ResourceTypeAtPath(context.Request.Path);

            var dataReq = new CalendarCompRequest();
            var resps = new List<Response>();

            switch (resType)
            {
                case ResourceType.Root:
                    resps.Add(PropFindRoot(context, propfind));
                    break;
                case ResourceType.UserPrincipal:
                    var principalPath = Backend.CurrentUserPrincipal();
                    if (context.Request.Path == principalPath)
                    {
                        resps.Add(PropFindUserPrincipal(context, propfind));
                        if (depth != Depth.Zero)
                        {
                            resps.Add(PropFindHomeSet(context, propfind));
                            if (depth == Depth.Infinity)
                            {
                                resps.AddRange(PropFindAllCalendars(context, propfind, true));
                            }
                        }
                    }
                    break;
                case ResourceType.CalendarHomeSet:
                    var homeSetPath = Backend.CalendarHomeSetPath();
                    if (context.Request.Path == homeSetPath)
                    {
                        resps.Add(PropFindHomeSet(context, propfind));
                        if (depth != Depth.Zero)
                        {
                            var recurse = depth == Depth.Infinity;
                            resps.AddRange(PropFindAllCalendars(context, propfind, recurse));
                        }
                    }
                    break;
                case ResourceType.Calendar:
                    var cal = Backend.GetCalendar(context.Request.Path);
                    resps.Add(PropFindCalendar(context, propfind, cal));
                    if (depth != Depth.Zero)
                    {
                        resps.AddRange(PropFindAllCalendarObjects(context, propfind, cal));
                    }
                    break;
                case ResourceType.CalendarObject:
                    var co = Backend.GetCalendarObject(context.Request.Path, dataReq);
                    resps.Add(PropFindCalendarObject(context, propfind, co));
                    break;
            }

            return new MultiStatus(resps.ToArray());
        }

        private Response PropFindRoot(HttpContext context, PropFind propfind)
        {
            var principalPath = Backend.CurrentUserPrincipal();
            var props = new Dictionary<XmlQualifiedName, PropFindFunc>
            {
                { new XmlQualifiedName("current-user-principal", "DAV:"), () => new CurrentUserPrincipal { Href = principalPath } },
                { new XmlQualifiedName("resourcetype", "DAV:"), () => new ResourceType(new[] { "collection" }) }
            };
            return new Response(principalPath, propfind, props);
        }

        private Response PropFindUserPrincipal(HttpContext context, PropFind propfind)
        {
            var principalPath = Backend.CurrentUserPrincipal();
            var homeSetPath = Backend.CalendarHomeSetPath();
            var props = new Dictionary<XmlQualifiedName, PropFindFunc>
            {
                { new XmlQualifiedName("current-user-principal", "DAV:"), () => new CurrentUserPrincipal { Href = principalPath } },
                { new XmlQualifiedName("calendar-home-set", "urn:ietf:params:xml:ns:caldav"), () => new CalendarHomeSet { Href = homeSetPath } },
                { new XmlQualifiedName("resourcetype", "DAV:"), () => new ResourceType(new[] { "collection" }) }
            };
            return new Response(principalPath, propfind, props);
        }

        private Response PropFindHomeSet(HttpContext context, PropFind propfind)
        {
            var principalPath = Backend.CurrentUserPrincipal();
            var homeSetPath = Backend.CalendarHomeSetPath();
            var props = new Dictionary<XmlQualifiedName, PropFindFunc>
            {
                { new XmlQualifiedName("current-user-principal", "DAV:"), () => new CurrentUserPrincipal { Href = principalPath } },
                { new XmlQualifiedName("resourcetype", "DAV:"), () => new ResourceType(new[] { "collection" }) }
            };
            return new Response(homeSetPath, propfind, props);
        }

        private Response PropFindCalendar(HttpContext context, PropFind propfind, Calendar cal)
        {
            var props = new Dictionary<XmlQualifiedName, PropFindFunc>
            {
                { new XmlQualifiedName("current-user-principal", "DAV:"), () => new CurrentUserPrincipal { Href = Backend.CurrentUserPrincipal() } },
                { new XmlQualifiedName("resourcetype", "DAV:"), () => new ResourceType(new[] { "collection", "calendar" }) },
                { new XmlQualifiedName("calendar-description", "urn:ietf:params:xml:ns:caldav"), () => new CalendarDescription { Description = cal.Description } },
                { new XmlQualifiedName("supported-calendar-data", "urn:ietf:params:xml:ns:caldav"), () => new SupportedCalendarData { Types = new List<CalendarDataType> { new CalendarDataType { ContentType = "text/calendar", Version = "2.0" } } } },
                { new XmlQualifiedName("supported-calendar-component-set", "urn:ietf:params:xml:ns:caldav"), () => new SupportedCalendarComponentSet { Comp = cal.SupportedComponentSet?.Select(name => new Comp { Name = name }).ToList() ?? new List<Comp> { new Comp { Name = "VEVENT" } } } }
            };

            if (!string.IsNullOrEmpty(cal.Name))
            {
                props[new XmlQualifiedName("displayname", "DAV:")] = () => new DisplayName { Name = cal.Name };
            }
            if (!string.IsNullOrEmpty(cal.Description))
            {
                props[new XmlQualifiedName("calendar-description", "urn:ietf:params:xml:ns:caldav")] = () => new CalendarDescription { Description = cal.Description };
            }
            if (cal.MaxResourceSize > 0)
            {
                props[new XmlQualifiedName("max-resource-size", "urn:ietf:params:xml:ns:caldav")] = () => new MaxResourceSize { Size = cal.MaxResourceSize };
            }

            return new Response(cal.Path, propfind, props);
        }

        private List<Response> PropFindAllCalendars(HttpContext context, PropFind propfind, bool recurse)
        {
            var abs = Backend.ListCalendars();
            var resps = new List<Response>();
            foreach (var ab in abs)
            {
                resps.Add(PropFindCalendar(context, propfind, ab));
                if (recurse)
                {
                    resps.AddRange(PropFindAllCalendarObjects(context, propfind, ab));
                }
            }
            return resps;
        }

        private Response PropFindCalendarObject(HttpContext context, PropFind propfind, CalendarObject co)
        {
            var props = new Dictionary<XmlQualifiedName, PropFindFunc>
            {
                { new XmlQualifiedName("current-user-principal", "DAV:"), () => new CurrentUserPrincipal { Href = Backend.CurrentUserPrincipal() } },
                { new XmlQualifiedName("getcontenttype", "DAV:"), () => new GetContentType { Type = "text/calendar" } },
                { new XmlQualifiedName("calendar-data", "urn:ietf:params:xml:ns:caldav"), () => new CalendarDataResp { Data = Encoding.UTF8.GetBytes(new CalendarSerializer().SerializeToString(co.Data)) } }
            };

            if (co.ContentLength > 0)
            {
                props[new XmlQualifiedName("getcontentlength", "DAV:")] = () => new GetContentLength { Length = co.ContentLength };
            }
            if (co.ModTime != DateTime.MinValue)
            {
                props[new XmlQualifiedName("getlastmodified", "DAV:")] = () => new GetLastModified { LastModified = co.ModTime };
            }
            if (!string.IsNullOrEmpty(co.ETag))
            {
                props[new XmlQualifiedName("getetag", "DAV:")] = () => new GetETag { ETag = co.ETag };
            }

            return new Response(co.Path, propfind, props);
        }

        private List<Response> PropFindAllCalendarObjects(HttpContext context, PropFind propfind, Calendar cal)
        {
            var dataReq = new CalendarCompRequest();
            var aos = Backend.ListCalendarObjects(cal.Path, dataReq);
            var resps = new List<Response>();
            foreach (var ao in aos)
            {
                resps.Add(PropFindCalendarObject(context, propfind, ao));
            }
            return resps;
        }

        public override Response PropPatch(HttpContext context, PropertyUpdate update)
        {
            throw new NotImplementedException("caldav: PropPatch not implemented");
        }

        public override void Put(HttpContext context)
        {
            var ifNoneMatch = new ConditionalMatch(context.Request.Headers["If-None-Match"]);
            var ifMatch = new ConditionalMatch(context.Request.Headers["If-Match"]);

            var opts = new PutCalendarObjectOptions
            {
                IfNoneMatch = ifNoneMatch,
                IfMatch = ifMatch
            };

            var t = context.Request.ContentType;
            if (t != "text/calendar")
            {
                throw new InvalidOperationException($"caldav: unsupported Content-Type {t}");
            }

            var serializer = new CalendarSerializer();
            var cal = serializer.Deserialize(new StreamReader(context.Request.InputStream).ReadToEnd());

            var co = Backend.PutCalendarObject(context.Request.Path, cal, opts);

            if (!string.IsNullOrEmpty(co.ETag))
            {
                context.Response.Headers["ETag"] = co.ETag;
            }
            if (co.ModTime != DateTime.MinValue)
            {
                context.Response.Headers["Last-Modified"] = co.ModTime.ToUniversalTime().ToString("R");
            }
            if (!string.IsNullOrEmpty(co.Path))
            {
                context.Response.Headers["Location"] = co.Path;
            }

            context.Response.StatusCode = (int)HttpStatusCode.Created;
        }

        public override void Delete(HttpContext context)
        {
            Backend.DeleteCalendarObject(context.Request.Path);
        }

        public override void Mkcol(HttpContext context)
        {
            if (ResourceTypeAtPath(context.Request.Path) != ResourceType.Calendar)
            {
                throw new InvalidOperationException("caldav: calendar creation not allowed at given location");
            }

            var cal = new Calendar
            {
                Path = context.Request.Path
            };

            if (context.Request.InputStream.Length > 0)
            {
                var m = Deserialize<MkcolReq>(context.Request.InputStream);
                if (!m.ResourceType.IsCollection || !m.ResourceType.IsCalendar)
                {
                    throw new InvalidOperationException("caldav: unexpected resource type");
                }
                cal.Name = m.DisplayName;
            }

            Backend.CreateCalendar(cal);
        }

        public override bool Copy(HttpContext context, Href dest, bool recursive, bool overwrite)
        {
            throw new NotImplementedException("caldav: Copy not implemented");
        }

        public override bool Move(HttpContext context, Href dest, bool overwrite)
        {
            throw new NotImplementedException("caldav: Move not implemented");
        }

        private ResourceType ResourceTypeAtPath(string reqPath)
        {
            var p = reqPath.TrimStart('/');
            if (string.IsNullOrEmpty(p))
            {
                return ResourceType.Root;
            }
            return (ResourceType)p.Split('/').Length;
        }
    }

    public enum ResourceType
    {
        Root,
        UserPrincipal,
        CalendarHomeSet,
        Calendar,
        CalendarObject
    }

    public class PreconditionError : Exception
    {
        public PreconditionType Precondition { get; }

        public PreconditionError(PreconditionType precondition)
        {
            Precondition = precondition;
        }

        public override string ToString()
        {
            return $"Precondition failed: {Precondition}";
        }
    }

    public enum PreconditionType
    {
        NoUIDConflict,
        SupportedCalendarData,
        SupportedCalendarComponent,
        ValidCalendarData,
        ValidCalendarObjectResource,
        CalendarCollectionLocationOk,
        MaxResourceSize,
        MinDateTime,
        MaxDateTime,
        MaxInstances,
        MaxAttendeesPerInstance
    }
}
