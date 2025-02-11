using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace WebDav
{
    public static class Server
    {
        public static void ServeError(HttpResponseMessage response, Exception err)
        {
            var code = HttpStatusCode.InternalServerError;
            if (err is HttpError httpErr)
            {
                code = (HttpStatusCode)httpErr.Code;
            }

            if (err is WebDavError errElt)
            {
                response.StatusCode = code;
                ServeXML(response).WriteObject(errElt);
                return;
            }

            response.Content = new StringContent(err.Message);
            response.StatusCode = code;
        }

        private static bool IsContentXML(HttpHeaders headers)
        {
            var contentType = headers.ContentType?.MediaType;
            return contentType == "application/xml" || contentType == "text/xml";
        }

        public static async Task DecodeXMLRequest(HttpRequestMessage request, object v)
        {
            if (!IsContentXML(request.Headers))
            {
                throw new HttpError(HttpStatusCode.BadRequest, "webdav: expected application/xml request");
            }

            using (var stream = await request.Content.ReadAsStreamAsync())
            {
                var serializer = new System.Xml.Serialization.XmlSerializer(v.GetType());
                v = serializer.Deserialize(stream);
            }
        }

        public static bool IsRequestBodyEmpty(HttpRequestMessage request)
        {
            return request.Content == null || request.Content.Headers.ContentLength == 0;
        }

        public static System.Xml.Serialization.XmlSerializer ServeXML(HttpResponseMessage response)
        {
            response.Headers.Add("Content-Type", "application/xml; charset=\"utf-8\"");
            return new System.Xml.Serialization.XmlSerializer(typeof(object));
        }

        public static async Task ServeMultiStatus(HttpResponseMessage response, MultiStatus ms)
        {
            response.StatusCode = HttpStatusCode.MultiStatus;
            var serializer = ServeXML(response);
            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, ms);
                stream.Position = 0;
                response.Content = new StreamContent(stream);
            }
        }

        public interface IBackend
        {
            Task<(List<string> caps, List<string> allow)> Options(HttpRequestMessage request);
            Task HeadGet(HttpResponseMessage response, HttpRequestMessage request);
            Task<MultiStatus> PropFind(HttpRequestMessage request, PropFind pf, Depth depth);
            Task<Response> PropPatch(HttpRequestMessage request, PropertyUpdate pu);
            Task Put(HttpResponseMessage response, HttpRequestMessage request);
            Task Delete(HttpRequestMessage request);
            Task Mkcol(HttpRequestMessage request);
            Task<(bool created, Exception err)> Copy(HttpRequestMessage request, Href dest, bool recursive, bool overwrite);
            Task<(bool created, Exception err)> Move(HttpRequestMessage request, Href dest, bool overwrite);
        }

        public class Handler : DelegatingHandler
        {
            public IBackend Backend { get; set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage();
                try
                {
                    if (Backend == null)
                    {
                        throw new Exception("webdav: no backend available");
                    }

                    switch (request.Method.Method)
                    {
                        case "OPTIONS":
                            await HandleOptions(response, request);
                            break;
                        case "GET":
                        case "HEAD":
                            await Backend.HeadGet(response, request);
                            break;
                        case "PUT":
                            await Backend.Put(response, request);
                            break;
                        case "DELETE":
                            await Backend.Delete(request);
                            response.StatusCode = HttpStatusCode.NoContent;
                            break;
                        case "PROPFIND":
                            await HandlePropfind(response, request);
                            break;
                        case "PROPPATCH":
                            await HandleProppatch(response, request);
                            break;
                        case "MKCOL":
                            await Backend.Mkcol(request);
                            response.StatusCode = HttpStatusCode.Created;
                            break;
                        case "COPY":
                        case "MOVE":
                            await HandleCopyMove(response, request);
                            break;
                        default:
                            throw new HttpError(HttpStatusCode.MethodNotAllowed, "webdav: unsupported method");
                    }
                }
                catch (Exception ex)
                {
                    ServeError(response, ex);
                }

                return response;
            }

            private async Task HandleOptions(HttpResponseMessage response, HttpRequestMessage request)
            {
                var (caps, allow) = await Backend.Options(request);
                caps.Insert(0, "1");
                caps.Insert(1, "3");

                response.Headers.Add("DAV", string.Join(", ", caps));
                response.Headers.Add("Allow", string.Join(", ", allow));
                response.StatusCode = HttpStatusCode.NoContent;
            }

            private async Task HandlePropfind(HttpResponseMessage response, HttpRequestMessage request)
            {
                PropFind propfind;
                if (IsContentXML(request.Headers))
                {
                    propfind = new PropFind();
                    await DecodeXMLRequest(request, propfind);
                }
                else
                {
                    if (!IsRequestBodyEmpty(request))
                    {
                        throw new HttpError(HttpStatusCode.BadRequest, "webdav: unsupported request body");
                    }
                    propfind = new PropFind { AllProp = new object() };
                }

                var depth = Depth.Infinity;
                if (request.Headers.TryGetValues("Depth", out var depthValues))
                {
                    var depthStr = depthValues.FirstOrDefault();
                    if (!string.IsNullOrEmpty(depthStr))
                    {
                        depth = Depth.Parse(depthStr);
                    }
                }

                var ms = await Backend.PropFind(request, propfind, depth);
                await ServeMultiStatus(response, ms);
            }

            private async Task HandleProppatch(HttpResponseMessage response, HttpRequestMessage request)
            {
                var update = new PropertyUpdate();
                await DecodeXMLRequest(request, update);

                var resp = await Backend.PropPatch(request, update);
                var ms = new MultiStatus { Responses = new List<Response> { resp } };
                await ServeMultiStatus(response, ms);
            }

            private async Task HandleCopyMove(HttpResponseMessage response, HttpRequestMessage request)
            {
                var dest = ParseDestination(request.Headers);
                var overwrite = true;
                if (request.Headers.TryGetValues("Overwrite", out var overwriteValues))
                {
                    var overwriteStr = overwriteValues.FirstOrDefault();
                    if (!string.IsNullOrEmpty(overwriteStr))
                    {
                        overwrite = Depth.ParseOverwrite(overwriteStr);
                    }
                }

                var depth = Depth.Infinity;
                if (request.Headers.TryGetValues("Depth", out var depthValues))
                {
                    var depthStr = depthValues.FirstOrDefault();
                    if (!string.IsNullOrEmpty(depthStr))
                    {
                        depth = Depth.Parse(depthStr);
                    }
                }

                bool created;
                if (request.Method.Method == "COPY")
                {
                    var recursive = depth == Depth.Infinity;
                    if (depth == Depth.One)
                    {
                        throw new HttpError(HttpStatusCode.BadRequest, "webdav: \"Depth: 1\" is not supported in COPY request");
                    }

                    (created, var err) = await Backend.Copy(request, dest, recursive, overwrite);
                    if (err != null)
                    {
                        throw err;
                    }
                }
                else
                {
                    if (depth != Depth.Infinity)
                    {
                        throw new HttpError(HttpStatusCode.BadRequest, "webdav: only \"Depth: infinity\" is accepted in MOVE request");
                    }

                    (created, var err) = await Backend.Move(request, dest, overwrite);
                    if (err != null)
                    {
                        throw err;
                    }
                }

                response.StatusCode = created ? HttpStatusCode.Created : HttpStatusCode.NoContent;
            }

            private Href ParseDestination(HttpHeaders headers)
            {
                var destHref = headers.GetValues("Destination").FirstOrDefault();
                if (string.IsNullOrEmpty(destHref))
                {
                    throw new HttpError(HttpStatusCode.BadRequest, "webdav: missing Destination header in MOVE request");
                }

                if (!Uri.TryCreate(destHref, UriKind.RelativeOrAbsolute, out var dest))
                {
                    throw new HttpError(HttpStatusCode.BadRequest, $"webdav: malformed Destination header in MOVE request: {destHref}");
                }

                return new Href(dest);
            }
        }
    }
}
