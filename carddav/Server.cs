using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using Ical.Net;
using Ical.Net.Serialization;
using WebDav;

namespace CardDav
{
    public class PutAddressObjectOptions
    {
        public ConditionalMatch IfNoneMatch { get; set; }
        public ConditionalMatch IfMatch { get; set; }
    }

    public interface Backend
    {
        Task<string> AddressBookHomeSetPathAsync(HttpContext context);
        Task<List<AddressBook>> ListAddressBooksAsync(HttpContext context);
        Task<AddressBook> GetAddressBookAsync(HttpContext context, string path);
        Task CreateAddressBookAsync(HttpContext context, AddressBook addressBook);
        Task DeleteAddressBookAsync(HttpContext context, string path);
        Task<AddressObject> GetAddressObjectAsync(HttpContext context, string path, AddressDataRequest req);
        Task<List<AddressObject>> ListAddressObjectsAsync(HttpContext context, string path, AddressDataRequest req);
        Task<List<AddressObject>> QueryAddressObjectsAsync(HttpContext context, string path, AddressBookQuery query);
        Task<AddressObject> PutAddressObjectAsync(HttpContext context, string path, VCard card, PutAddressObjectOptions opts);
        Task DeleteAddressObjectAsync(HttpContext context, string path);

        Task<string> CurrentUserPrincipalAsync(HttpContext context);
    }

    public class Handler : HttpHandler
    {
        public Backend Backend { get; set; }
        public string Prefix { get; set; }

        public override async Task HandleRequestAsync(HttpContext context)
        {
            if (Backend == null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await context.Response.WriteAsync("carddav: no backend available");
                return;
            }

            if (context.Request.Path == "/.well-known/carddav")
            {
                var principalPath = await Backend.CurrentUserPrincipalAsync(context);
                context.Response.Redirect(principalPath, true);
                return;
            }

            try
            {
                switch (context.Request.Method)
                {
                    case "REPORT":
                        await HandleReportAsync(context);
                        break;
                    default:
                        var b = new BackendWrapper
                        {
                            Backend = Backend,
                            Prefix = Prefix.TrimEnd('/')
                        };
                        var hh = new InternalHandler { Backend = b };
                        await hh.HandleRequestAsync(context);
                        break;
                }
            }
            catch (Exception ex)
            {
                await InternalHandler.ServeErrorAsync(context.Response, ex);
            }
        }

        private async Task HandleReportAsync(HttpContext context)
        {
            var report = await InternalHandler.DecodeXmlRequestAsync<ReportReq>(context.Request);
            if (report.Query != null)
            {
                await HandleQueryAsync(context, report.Query);
            }
            else if (report.Multiget != null)
            {
                await HandleMultigetAsync(context, report.Multiget);
            }
            else
            {
                throw new HttpException(HttpStatusCode.BadRequest, "carddav: expected addressbook-query or addressbook-multiget element in REPORT request");
            }
        }

        private async Task HandleQueryAsync(HttpContext context, AddressbookQuery query)
        {
            var q = new AddressBookQuery
            {
                DataRequest = await DecodeAddressDataReqAsync(query.Prop),
                FilterTest = (FilterTest)Enum.Parse(typeof(FilterTest), query.Filter.Test, true)
            };

            foreach (var el in query.Filter.Props)
            {
                var pf = await DecodePropFilterAsync(el);
                q.PropFilters.Add(pf);
            }

            if (query.Limit != null)
            {
                q.Limit = (int)query.Limit.NResults;
                if (q.Limit <= 0)
                {
                    await InternalHandler.ServeMultiStatusAsync(context.Response, new MultiStatus());
                    return;
                }
            }

            var aos = await Backend.QueryAddressObjectsAsync(context, context.Request.Path, q);
            var resps = new List<Response>();

            foreach (var ao in aos)
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
                var resp = await b.PropFindAddressObjectAsync(context, propfind, ao);
                resps.Add(resp);
            }

            var ms = new MultiStatus(resps);
            await InternalHandler.ServeMultiStatusAsync(context.Response, ms);
        }

        private async Task HandleMultigetAsync(HttpContext context, AddressbookMultiget multiget)
        {
            var dataReq = await DecodeAddressDataReqAsync(multiget.Prop);
            var resps = new List<Response>();

            foreach (var href in multiget.Hrefs)
            {
                try
                {
                    var ao = await Backend.GetAddressObjectAsync(context, href.Path, dataReq);
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
                    var resp = await b.PropFindAddressObjectAsync(context, propfind, ao);
                    resps.Add(resp);
                }
                catch (Exception ex)
                {
                    var resp = InternalHandler.NewErrorResponse(href.Path, ex);
                    resps.Add(resp);
                }
            }

            var ms = new MultiStatus(resps);
            await InternalHandler.ServeMultiStatusAsync(context.Response, ms);
        }

        private async Task<AddressDataRequest> DecodeAddressDataReqAsync(AddressDataReq addressData)
        {
            if (addressData.Allprop != null && addressData.Props.Count > 0)
            {
                throw new HttpException(HttpStatusCode.BadRequest, "carddav: only one of allprop or prop can be specified in address-data");
            }

            var req = new AddressDataRequest { AllProp = addressData.Allprop != null };
            req.Props.AddRange(addressData.Props.Select(p => p.Name));
            return req;
        }

        private async Task<PropFilter> DecodePropFilterAsync(PropFilterEl el)
        {
            var pf = new PropFilter
            {
                Name = el.Name,
                Test = (FilterTest)Enum.Parse(typeof(FilterTest), el.Test, true)
            };

            if (el.IsNotDefined != null)
            {
                if (el.TextMatches.Count > 0 || el.Params.Count > 0)
                {
                    throw new HttpException(HttpStatusCode.BadRequest, "carddav: failed to parse prop-filter: if is-not-defined is provided, text-match or param-filter can't be provided");
                }
                pf.IsNotDefined = true;
            }

            foreach (var tm in el.TextMatches)
            {
                pf.TextMatches.Add(DecodeTextMatch(tm));
            }

            foreach (var paramEl in el.Params)
            {
                var param = await DecodeParamFilterAsync(paramEl);
                pf.Params.Add(param);
            }

            return pf;
        }

        private TextMatch DecodeTextMatch(TextMatchEl tm)
        {
            return new TextMatch
            {
                Text = tm.Text,
                NegateCondition = tm.NegateCondition,
                MatchType = (MatchType)Enum.Parse(typeof(MatchType), tm.MatchType, true)
            };
        }

        private async Task<ParamFilter> DecodeParamFilterAsync(ParamFilterEl el)
        {
            var pf = new ParamFilter { Name = el.Name };

            if (el.IsNotDefined != null)
            {
                if (el.TextMatch != null)
                {
                    throw new HttpException(HttpStatusCode.BadRequest, "carddav: failed to parse param-filter: if is-not-defined is provided, text-match can't be provided");
                }
                pf.IsNotDefined = true;
            }

            if (el.TextMatch != null)
            {
                pf.TextMatch = DecodeTextMatch(el.TextMatch);
            }

            return pf;
        }
    }

    public class BackendWrapper : Backend
    {
        public Backend Backend { get; set; }
        public string Prefix { get; set; }

        public async Task<string> AddressBookHomeSetPathAsync(HttpContext context)
        {
            return await Backend.AddressBookHomeSetPathAsync(context);
        }

        public async Task<List<AddressBook>> ListAddressBooksAsync(HttpContext context)
        {
            return await Backend.ListAddressBooksAsync(context);
        }

        public async Task<AddressBook> GetAddressBookAsync(HttpContext context, string path)
        {
            return await Backend.GetAddressBookAsync(context, path);
        }

        public async Task CreateAddressBookAsync(HttpContext context, AddressBook addressBook)
        {
            await Backend.CreateAddressBookAsync(context, addressBook);
        }

        public async Task DeleteAddressBookAsync(HttpContext context, string path)
        {
            await Backend.DeleteAddressBookAsync(context, path);
        }

        public async Task<AddressObject> GetAddressObjectAsync(HttpContext context, string path, AddressDataRequest req)
        {
            return await Backend.GetAddressObjectAsync(context, path, req);
        }

        public async Task<List<AddressObject>> ListAddressObjectsAsync(HttpContext context, string path, AddressDataRequest req)
        {
            return await Backend.ListAddressObjectsAsync(context, path, req);
        }

        public async Task<List<AddressObject>> QueryAddressObjectsAsync(HttpContext context, string path, AddressBookQuery query)
        {
            return await Backend.QueryAddressObjectsAsync(context, path, query);
        }

        public async Task<AddressObject> PutAddressObjectAsync(HttpContext context, string path, VCard card, PutAddressObjectOptions opts)
        {
            return await Backend.PutAddressObjectAsync(context, path, card, opts);
        }

        public async Task DeleteAddressObjectAsync(HttpContext context, string path)
        {
            await Backend.DeleteAddressObjectAsync(context, path);
        }

        public async Task<string> CurrentUserPrincipalAsync(HttpContext context)
        {
            return await Backend.CurrentUserPrincipalAsync(context);
        }

        public async Task<List<string>> OptionsAsync(HttpContext context)
        {
            var caps = new List<string> { "addressbook" };

            if (ResourceTypeAtPath(context.Request.Path) != ResourceType.AddressObject)
            {
                return new List<string> { "OPTIONS", "PROPFIND", "REPORT", "DELETE", "MKCOL" };
            }

            var dataReq = new AddressDataRequest();
            try
            {
                await Backend.GetAddressObjectAsync(context, context.Request.Path, dataReq);
                return new List<string> { "OPTIONS", "HEAD", "GET", "PUT", "DELETE", "PROPFIND" };
            }
            catch (HttpException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return new List<string> { "OPTIONS", "PUT" };
            }
        }

        public async Task HeadGetAsync(HttpContext context)
        {
            var dataReq = new AddressDataRequest();
            if (context.Request.Method != "HEAD")
            {
                dataReq.AllProp = true;
            }

            var ao = await Backend.GetAddressObjectAsync(context, context.Request.Path, dataReq);
            context.Response.ContentType = VCard.MimeType;

            if (ao.ContentLength > 0)
            {
                context.Response.ContentLength = ao.ContentLength;
            }

            if (!string.IsNullOrEmpty(ao.ETag))
            {
                context.Response.Headers["ETag"] = ETag(ao.ETag).ToString();
            }

            if (ao.ModTime != DateTime.MinValue)
            {
                context.Response.Headers["Last-Modified"] = ao.ModTime.ToUniversalTime().ToString("R");
            }

            if (context.Request.Method != "HEAD")
            {
                await VCard.EncodeAsync(context.Response.Body, ao.Card);
            }
        }

        public async Task<MultiStatus> PropFindAsync(HttpContext context, PropFind propfind, Depth depth)
        {
            var resType = ResourceTypeAtPath(context.Request.Path);
            var dataReq = new AddressDataRequest();
            var resps = new List<Response>();

            switch (resType)
            {
                case ResourceType.Root:
                    var rootResp = await PropFindRootAsync(context, propfind);
                    resps.Add(rootResp);
                    break;
                case ResourceType.UserPrincipal:
                    var principalPath = await Backend.CurrentUserPrincipalAsync(context);
                    if (context.Request.Path == principalPath)
                    {
                        var userPrincipalResp = await PropFindUserPrincipalAsync(context, propfind);
                        resps.Add(userPrincipalResp);

                        if (depth != Depth.Zero)
                        {
                            var homeSetResp = await PropFindHomeSetAsync(context, propfind);
                            resps.Add(homeSetResp);

                            if (depth == Depth.Infinity)
                            {
                                var allAddressBooksResps = await PropFindAllAddressBooksAsync(context, propfind, true);
                                resps.AddRange(allAddressBooksResps);
                            }
                        }
                    }
                    break;
                case ResourceType.AddressBookHomeSet:
                    var homeSetPath = await Backend.AddressBookHomeSetPathAsync(context);
                    if (context.Request.Path == homeSetPath)
                    {
                        var homeSetResp = await PropFindHomeSetAsync(context, propfind);
                        resps.Add(homeSetResp);

                        if (depth != Depth.Zero)
                        {
                            var allAddressBooksResps = await PropFindAllAddressBooksAsync(context, propfind, depth == Depth.Infinity);
                            resps.AddRange(allAddressBooksResps);
                        }
                    }
                    break;
                case ResourceType.AddressBook:
                    var ab = await Backend.GetAddressBookAsync(context, context.Request.Path);
                    var addressBookResp = await PropFindAddressBookAsync(context, propfind, ab);
                    resps.Add(addressBookResp);

                    if (depth != Depth.Zero)
                    {
                        var allAddressObjectsResps = await PropFindAllAddressObjectsAsync(context, propfind, ab);
                        resps.AddRange(allAddressObjectsResps);
                    }
                    break;
                case ResourceType.AddressObject:
                    var ao = await Backend.GetAddressObjectAsync(context, context.Request.Path, dataReq);
                    var addressObjectResp = await PropFindAddressObjectAsync(context, propfind, ao);
                    resps.Add(addressObjectResp);
                    break;
            }

            return new MultiStatus(resps);
        }

        private async Task<Response> PropFindRootAsync(HttpContext context, PropFind propfind)
        {
            var principalPath = await Backend.CurrentUserPrincipalAsync(context);
            var props = new Dictionary<XName, Func<RawXmlValue, Task<object>>>
            {
                { WebDavNames.CurrentUserPrincipal, _ => Task.FromResult<object>(new CurrentUserPrincipal { Href = new Href { Path = principalPath } }) },
                { WebDavNames.ResourceType, _ => Task.FromResult<object>(new ResourceType { Collection = true }) }
            };

            return await InternalHandler.NewPropFindResponseAsync(principalPath, propfind, props);
        }

        private async Task<Response> PropFindUserPrincipalAsync(HttpContext context, PropFind propfind)
        {
            var principalPath = await Backend.CurrentUserPrincipalAsync(context);
            var props = new Dictionary<XName, Func<RawXmlValue, Task<object>>>
            {
                { WebDavNames.CurrentUserPrincipal, _ => Task.FromResult<object>(new CurrentUserPrincipal { Href = new Href { Path = principalPath } }) },
                { CardDavNames.AddressBookHomeSet, async _ => new AddressbookHomeSet { Href = new Href { Path = await Backend.AddressBookHomeSetPathAsync(context) } } },
                { WebDavNames.ResourceType, _ => Task.FromResult<object>(new ResourceType { Collection = true }) }
            };

            return await InternalHandler.NewPropFindResponseAsync(principalPath, propfind, props);
        }

        private async Task<Response> PropFindHomeSetAsync(HttpContext context, PropFind propfind)
        {
            var homeSetPath = await Backend.AddressBookHomeSetPathAsync(context);
            var props = new Dictionary<XName, Func<RawXmlValue, Task<object>>>
            {
                { WebDavNames.CurrentUserPrincipal, async _ => new CurrentUserPrincipal { Href = new Href { Path = await Backend.CurrentUserPrincipalAsync(context) } } },
                { WebDavNames.ResourceType, _ => Task.FromResult<object>(new ResourceType { Collection = true }) }
            };

            return await InternalHandler.NewPropFindResponseAsync(homeSetPath, propfind, props);
        }

        private async Task<Response> PropFindAddressBookAsync(HttpContext context, PropFind propfind, AddressBook ab)
        {
            var props = new Dictionary<XName, Func<RawXmlValue, Task<object>>>
            {
                { WebDavNames.CurrentUserPrincipal, async _ => new CurrentUserPrincipal { Href = new Href { Path = await Backend.CurrentUserPrincipalAsync(context) } } },
                { WebDavNames.ResourceType, _ => Task.FromResult<object>(new ResourceType { Collection = true, AddressBook = true }) },
                { CardDavNames.SupportedAddressData, _ => Task.FromResult<object>(new SupportedAddressData { Types = new List<AddressDataType> { new AddressDataType { ContentType = VCard.MimeType, Version = "3.0" }, new AddressDataType { ContentType = VCard.MimeType, Version = "4.0" } } }) }
            };

            if (!string.IsNullOrEmpty(ab.Name))
            {
                props[WebDavNames.DisplayName] = _ => Task.FromResult<object>(new DisplayName { Name = ab.Name });
            }

            if (!string.IsNullOrEmpty(ab.Description))
            {
                props[CardDavNames.AddressBookDescription] = _ => Task.FromResult<object>(new AddressbookDescription { Description = ab.Description });
            }

            if (ab.MaxResourceSize > 0)
            {
                props[CardDavNames.MaxResourceSize] = _ => Task.FromResult<object>(new MaxResourceSize { Size = ab.MaxResourceSize });
            }

            return await InternalHandler.NewPropFindResponseAsync(ab.Path, propfind, props);
        }

        private async Task<List<Response>> PropFindAllAddressBooksAsync(HttpContext context, PropFind propfind, bool recurse)
        {
            var abs = await Backend.ListAddressBooksAsync(context);
            var resps = new List<Response>();

            foreach (var ab in abs)
            {
                var resp = await PropFindAddressBookAsync(context, propfind, ab);
                resps.Add(resp);

                if (recurse)
                {
                    var allAddressObjectsResps = await PropFindAllAddressObjectsAsync(context, propfind, ab);
                    resps.AddRange(allAddressObjectsResps);
                }
            }

            return resps;
        }

        private async Task<Response> PropFindAddressObjectAsync(HttpContext context, PropFind propfind, AddressObject ao)
        {
            var props = new Dictionary<XName, Func<RawXmlValue, Task<object>>>
            {
                { WebDavNames.CurrentUserPrincipal, async _ => new CurrentUserPrincipal { Href = new Href { Path = await Backend.CurrentUserPrincipalAsync(context) } } },
                { WebDavNames.GetContentType, _ => Task.FromResult<object>(new GetContentType { Type = VCard.MimeType }) },
                { CardDavNames.AddressData, async _ => new AddressDataResp { Data = await VCard.EncodeAsync(ao.Card) } }
            };

            if (ao.ContentLength > 0)
            {
                props[WebDavNames.GetContentLength] = _ => Task.FromResult<object>(new GetContentLength { Length = ao.ContentLength });
            }

            if (ao.ModTime != DateTime.MinValue)
            {
                props[WebDavNames.GetLastModified] = _ => Task.FromResult<object>(new GetLastModified { LastModified = ao.ModTime });
            }

            if (!string.IsNullOrEmpty(ao.ETag))
            {
                props[WebDavNames.GetETag] = _ => Task.FromResult<object>(new GetETag { ETag = ao.ETag });
            }

            return await InternalHandler.NewPropFindResponseAsync(ao.Path, propfind, props);
        }

        private async Task<List<Response>> PropFindAllAddressObjectsAsync(HttpContext context, PropFind propfind, AddressBook ab)
        {
            var dataReq = new AddressDataRequest();
            var aos = await Backend.ListAddressObjectsAsync(context, ab.Path, dataReq);
            var resps = new List<Response>();

            foreach (var ao in aos)
            {
                var resp = await PropFindAddressObjectAsync(context, propfind, ao);
                resps.Add(resp);
            }

            return resps;
        }

        public async Task<Response> PropPatchAsync(HttpContext context, PropertyUpdate update)
        {
            var homeSetPath = await Backend.AddressBookHomeSetPathAsync(context);
            var resp = InternalHandler.NewOkResponse(context.Request.Path);

            if (context.Request.Path == homeSetPath)
            {
                foreach (var prop in update.Remove)
                {
                    var emptyVal = new RawXmlValue(prop.Prop.Name, null, null);
                    await resp.EncodePropAsync(HttpStatusCode.NotImplemented, emptyVal);
                }

                foreach (var prop in update.Set)
                {
                    var emptyVal = new RawXmlValue(prop.Prop.Name, null, null);
                    await resp.EncodePropAsync(HttpStatusCode.NotImplemented, emptyVal);
                }
            }
            else
            {
                foreach (var prop in update.Remove)
                {
                    var emptyVal = new RawXmlValue(prop.Prop.Name, null, null);
                    await resp.EncodePropAsync(HttpStatusCode.MethodNotAllowed, emptyVal);
                }

                foreach (var prop in update.Set)
                {
                    var emptyVal = new RawXmlValue(prop.Prop.Name, null, null);
                    await resp.EncodePropAsync(HttpStatusCode.MethodNotAllowed, emptyVal);
                }
            }

            return resp;
        }

        public async Task PutAsync(HttpContext context)
        {
            var ifNoneMatch = new ConditionalMatch(context.Request.Headers["If-None-Match"]);
            var ifMatch = new ConditionalMatch(context.Request.Headers["If-Match"]);

            var opts = new PutAddressObjectOptions
            {
                IfNoneMatch = ifNoneMatch,
                IfMatch = ifMatch
            };

            var t = context.Request.ContentType;
            if (!string.Equals(t, VCard.MimeType, StringComparison.OrdinalIgnoreCase))
            {
                throw new HttpException(HttpStatusCode.BadRequest, $"carddav: unsupported Content-Type {t}");
            }

            var card = await VCard.DecodeAsync(context.Request.Body);
            var ao = await Backend.PutAddressObjectAsync(context, context.Request.Path, card, opts);

            if (!string.IsNullOrEmpty(ao.ETag))
            {
                context.Response.Headers["ETag"] = ETag(ao.ETag).ToString();
            }

            if (ao.ModTime != DateTime.MinValue)
            {
                context.Response.Headers["Last-Modified"] = ao.ModTime.ToUniversalTime().ToString("R");
            }

            if (!string.IsNullOrEmpty(ao.Path))
            {
                context.Response.Headers["Location"] = ao.Path;
            }

            context.Response.StatusCode = (int)HttpStatusCode.Created;
        }

        public async Task DeleteAsync(HttpContext context)
        {
            switch (ResourceTypeAtPath(context.Request.Path))
            {
                case ResourceType.AddressBook:
                    await Backend.DeleteAddressBookAsync(context, context.Request.Path);
                    break;
                case ResourceType.AddressObject:
                    await Backend.DeleteAddressObjectAsync(context, context.Request.Path);
                    break;
                default:
                    throw new HttpException(HttpStatusCode.Forbidden, "carddav: cannot delete resource at given location");
            }
        }

        public async Task MkcolAsync(HttpContext context)
        {
            if (ResourceTypeAtPath(context.Request.Path) != ResourceType.AddressBook)
            {
                throw new HttpException(HttpStatusCode.Forbidden, "carddav: address book creation not allowed at given location");
            }

            var ab = new AddressBook { Path = context.Request.Path };

            if (!InternalHandler.IsRequestBodyEmpty(context.Request))
            {
                var m = await InternalHandler.DecodeXmlRequestAsync<MkcolReq>(context.Request);

                if (!m.ResourceType.IsCollection || !m.ResourceType.IsAddressBook)
                {
                    throw new HttpException(HttpStatusCode.BadRequest, "carddav: unexpected resource type");
                }

                ab.Name = m.DisplayName;
                ab.Description = m.Description.Description;
            }

            await Backend.CreateAddressBookAsync(context, ab);
        }

        public async Task<bool> CopyAsync(HttpContext context, Href dest, bool recursive, bool overwrite)
        {
            throw new HttpException(HttpStatusCode.NotImplemented, "carddav: Copy not implemented");
        }

        public async Task<bool> MoveAsync(HttpContext context, Href dest, bool overwrite)
        {
            throw new HttpException(HttpStatusCode.NotImplemented, "carddav: Move not implemented");
        }

        private ResourceType ResourceTypeAtPath(string reqPath)
        {
            var p = Path.Clean(reqPath);
            p = p.TrimStart(Prefix);
            if (!p.StartsWith("/"))
            {
                p = "/" + p;
            }

            if (p == "/")
            {
                return ResourceType.Root;
            }

            return (ResourceType)p.Split('/').Length - 1;
        }
    }

    public enum ResourceType
    {
        Root,
        UserPrincipal,
        AddressBookHomeSet,
        AddressBook,
        AddressObject
    }

    public class HttpException : Exception
    {
        public HttpStatusCode StatusCode { get; }

        public HttpException(HttpStatusCode statusCode, string message) : base(message)
        {
            StatusCode = statusCode;
        }
    }
}
