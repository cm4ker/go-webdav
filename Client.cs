using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Xml.Linq;
using WebDav;
using Internal;

namespace WebDav
{
    public interface IHttpClient
    {
        Task<HttpResponseMessage> SendAsync(HttpRequestMessage request);
    }

    public class BasicAuthHttpClient : IHttpClient
    {
        private readonly IHttpClient _client;
        private readonly string _username;
        private readonly string _password;

        public BasicAuthHttpClient(IHttpClient client, string username, string password)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _username = username ?? throw new ArgumentNullException(nameof(username));
            _password = password ?? throw new ArgumentNullException(nameof(password));
        }

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            var byteArray = System.Text.Encoding.ASCII.GetBytes($"{_username}:{_password}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
            return await _client.SendAsync(request);
        }
    }

    public static class HttpClientExtensions
    {
        public static IHttpClient WithBasicAuth(this IHttpClient client, string username, string password)
        {
            return new BasicAuthHttpClient(client, username, password);
        }
    }

    public class Client
    {
        private readonly Internal.Client _internalClient;

        public Client(IHttpClient httpClient, string endpoint)
        {
            _internalClient = new Internal.Client(httpClient, endpoint);
        }

        public async Task<string> FindCurrentUserPrincipalAsync()
        {
            var propfind = new PropFind
            {
                Names = new List<XName> { XName.Get("current-user-principal", "DAV:") }
            };

            var response = await _internalClient.PropFindAsync("", propfind);
            var prop = response.GetProperty("current-user-principal", "DAV:");

            if (prop == null || prop.Unauthenticated != null)
            {
                throw new InvalidOperationException("Unauthenticated");
            }

            return prop.Href.Path;
        }

        private static readonly PropFind FileInfoPropFind = new PropFind
        {
            Names = new List<XName>
            {
                XName.Get("resourcetype", "DAV:"),
                XName.Get("getcontentlength", "DAV:"),
                XName.Get("getlastmodified", "DAV:"),
                XName.Get("getcontenttype", "DAV:"),
                XName.Get("getetag", "DAV:")
            }
        };

        private static async Task<FileInfo> FileInfoFromResponseAsync(Response response)
        {
            var path = response.Href;
            var fi = new FileInfo { Path = path };

            var resType = response.GetProperty("resourcetype", "DAV:");
            if (resType != null && resType.IsCollection)
            {
                fi.IsDir = true;
            }
            else
            {
                var getLen = response.GetProperty("getcontentlength", "DAV:");
                var getType = response.GetProperty("getcontenttype", "DAV:");
                var getETag = response.GetProperty("getetag", "DAV:");

                fi.Size = getLen != null ? long.Parse(getLen.Value) : 0;
                fi.MIMEType = getType?.Value;
                fi.ETag = getETag?.Value;
            }

            var getMod = response.GetProperty("getlastmodified", "DAV:");
            fi.ModTime = getMod != null ? DateTime.Parse(getMod.Value) : DateTime.MinValue;

            return fi;
        }

        public async Task<FileInfo> StatAsync(string name)
        {
            var response = await _internalClient.PropFindFlatAsync(name, FileInfoPropFind);
            return await FileInfoFromResponseAsync(response);
        }

        public async Task<Stream> OpenAsync(string name)
        {
            var response = await _internalClient.GetAsync(name);
            return await response.Content.ReadAsStreamAsync();
        }

        public async Task<List<FileInfo>> ReadDirAsync(string name, bool recursive)
        {
            var depth = recursive ? Depth.Infinity : Depth.One;
            var response = await _internalClient.PropFindAsync(name, depth, FileInfoPropFind);

            var list = new List<FileInfo>();
            foreach (var resp in response.Responses)
            {
                var fi = await FileInfoFromResponseAsync(resp);
                list.Add(fi);
            }

            return list;
        }

        public async Task<Stream> CreateAsync(string name)
        {
            var content = new StreamContent(new MemoryStream());
            var response = await _internalClient.PutAsync(name, content);
            return await response.Content.ReadAsStreamAsync();
        }

        public async Task RemoveAllAsync(string name)
        {
            await _internalClient.DeleteAsync(name);
        }

        public async Task MkdirAsync(string name)
        {
            await _internalClient.MkcolAsync(name);
        }

        public async Task CopyAsync(string name, string dest, CopyOptions options = null)
        {
            options ??= new CopyOptions();
            var depth = options.NoRecursive ? Depth.Zero : Depth.Infinity;
            await _internalClient.CopyAsync(name, dest, depth, !options.NoOverwrite);
        }

        public async Task MoveAsync(string name, string dest, MoveOptions options = null)
        {
            options ??= new MoveOptions();
            await _internalClient.MoveAsync(name, dest, !options.NoOverwrite);
        }
    }
}
