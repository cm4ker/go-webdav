using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Internal
{
    public static class ServiceDiscovery
    {
        public static async Task<string> DiscoverContextUrlAsync(string service, string domain, CancellationToken cancellationToken = default)
        {
            var resolver = new DnsClient.LookupClient();

            var result = await resolver.QueryAsync($"_{service}s._tcp.{domain}", DnsClient.QueryType.SRV, DnsClient.QueryClass.IN, cancellationToken);
            var srvRecords = result.Answers.SrvRecords().ToList();

            if (!srvRecords.Any())
            {
                throw new Exception("webdav: domain doesn't have an SRV record");
            }

            var srvRecord = srvRecords.First();
            var target = srvRecord.Target.Value.TrimEnd('.');

            if (string.IsNullOrEmpty(target))
            {
                throw new Exception("webdav: empty target in SRV record");
            }

            var uriBuilder = new UriBuilder("https", target)
            {
                Port = srvRecord.Port,
                Path = $"/.well-known/{service}"
            };

            return uriBuilder.ToString();
        }
    }

    public interface IHttpClient
    {
        Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
    }

    public class Client
    {
        private readonly IHttpClient _httpClient;
        private readonly Uri _endpoint;

        public Client(IHttpClient httpClient, string endpoint)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _endpoint = new Uri(endpoint);
        }

        public Uri ResolveHref(string path)
        {
            if (!path.StartsWith("/"))
            {
                path = new Uri(_endpoint, path).AbsolutePath;
            }

            return new Uri(_endpoint, path);
        }

        public HttpRequestMessage NewRequest(HttpMethod method, string path, HttpContent content = null)
        {
            var request = new HttpRequestMessage(method, ResolveHref(path));
            if (content != null)
            {
                request.Content = content;
            }
            return request;
        }

        public async Task<HttpRequestMessage> NewXmlRequestAsync(HttpMethod method, string path, object content, CancellationToken cancellationToken = default)
        {
            var xmlContent = new StringContent(SerializeToXml(content));
            xmlContent.Headers.ContentType = new MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };

            var request = NewRequest(method, path, xmlContent);
            return await Task.FromResult(request);
        }

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "text/plain";
                var content = await response.Content.ReadAsStringAsync();

                if (contentType == "application/xml" || contentType == "text/xml")
                {
                    var davError = DeserializeFromXml<Error>(content);
                    throw new HttpRequestException($"HTTP {response.StatusCode}: {davError}");
                }
                else if (contentType.StartsWith("text/"))
                {
                    throw new HttpRequestException($"HTTP {response.StatusCode}: {content}");
                }
                else
                {
                    throw new HttpRequestException($"HTTP {response.StatusCode}");
                }
            }

            return response;
        }

        public async Task<MultiStatus> SendMultiStatusAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            var response = await SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.MultiStatus)
            {
                throw new HttpRequestException($"HTTP multi-status request failed: {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();
            return DeserializeFromXml<MultiStatus>(content);
        }

        public async Task<MultiStatus> PropFindAsync(string path, Depth depth, PropFind propfind, CancellationToken cancellationToken = default)
        {
            var request = await NewXmlRequestAsync(HttpMethod.PropFind, path, propfind, cancellationToken);
            request.Headers.Add("Depth", depth.ToString());

            return await SendMultiStatusAsync(request, cancellationToken);
        }

        public async Task<Response> PropFindFlatAsync(string path, PropFind propfind, CancellationToken cancellationToken = default)
        {
            var multiStatus = await PropFindAsync(path, Depth.Zero, propfind, cancellationToken);

            if (multiStatus.Responses.Count != 1)
            {
                throw new HttpRequestException($"PROPFIND with Depth: 0 returned {multiStatus.Responses.Count} responses");
            }

            return multiStatus.Responses.First();
        }

        public async Task<(Dictionary<string, bool> classes, Dictionary<string, bool> methods)> OptionsAsync(string path, CancellationToken cancellationToken = default)
        {
            var request = NewRequest(HttpMethod.Options, path);
            var response = await SendAsync(request, cancellationToken);

            var classes = ParseCommaSeparatedSet(response.Headers.GetValues("Dav"), false);
            if (!classes.ContainsKey("1"))
            {
                throw new HttpRequestException("webdav: server doesn't support DAV class 1");
            }

            var methods = ParseCommaSeparatedSet(response.Headers.GetValues("Allow"), true);
            return (classes, methods);
        }

        public async Task<MultiStatus> SyncCollectionAsync(string path, string syncToken, Depth level, Limit limit, Prop prop, CancellationToken cancellationToken = default)
        {
            var query = new SyncCollectionQuery
            {
                SyncToken = syncToken,
                SyncLevel = level.ToString(),
                Limit = limit,
                Prop = prop
            };

            var request = await NewXmlRequestAsync(new HttpMethod("REPORT"), path, query, cancellationToken);
            return await SendMultiStatusAsync(request, cancellationToken);
        }

        private static string SerializeToXml(object obj)
        {
            using (var stringWriter = new StringWriter())
            {
                var serializer = new System.Xml.Serialization.XmlSerializer(obj.GetType());
                serializer.Serialize(stringWriter, obj);
                return stringWriter.ToString();
            }
        }

        private static T DeserializeFromXml<T>(string xml)
        {
            using (var stringReader = new StringReader(xml))
            {
                var serializer = new System.Xml.Serialization.XmlSerializer(typeof(T));
                return (T)serializer.Deserialize(stringReader);
            }
        }

        private static Dictionary<string, bool> ParseCommaSeparatedSet(IEnumerable<string> values, bool upper)
        {
            var set = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
            {
                var fields = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var field in fields)
                {
                    set[upper ? field.ToUpperInvariant() : field.ToLowerInvariant()] = true;
                }
            }
            return set;
        }
    }
}
