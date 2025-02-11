using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Xml.Linq;
using VCard;
using WebDav;

namespace CardDav
{
    public class Client
    {
        private readonly WebDavClient _webDavClient;

        public Client(HttpClient httpClient, string endpoint)
        {
            _webDavClient = new WebDavClient(httpClient, endpoint);
        }

        public async Task<string> FindAddressBookHomeSetAsync(string principal)
        {
            var propfind = new PropFind
            {
                Names = new List<XName> { XName.Get("addressbook-home-set", "urn:ietf:params:xml:ns:carddav") }
            };

            var response = await _webDavClient.PropFindAsync(principal, propfind);
            var prop = response.GetProperty("addressbook-home-set", "urn:ietf:params:xml:ns:carddav");

            return prop?.Value;
        }

        public async Task<List<AddressBook>> FindAddressBooksAsync(string addressBookHomeSet)
        {
            var propfind = new PropFind
            {
                Names = new List<XName>
                {
                    XName.Get("resourcetype", "DAV:"),
                    XName.Get("displayname", "DAV:"),
                    XName.Get("addressbook-description", "urn:ietf:params:xml:ns:carddav"),
                    XName.Get("max-resource-size", "urn:ietf:params:xml:ns:carddav"),
                    XName.Get("supported-address-data", "urn:ietf:params:xml:ns:carddav")
                }
            };

            var response = await _webDavClient.PropFindAsync(addressBookHomeSet, propfind);
            var addressBooks = new List<AddressBook>();

            foreach (var resp in response.Responses)
            {
                var path = resp.Href;
                var resType = resp.GetProperty("resourcetype", "DAV:");
                if (resType == null || !resType.Value.Contains("addressbook"))
                {
                    continue;
                }

                var desc = resp.GetProperty("addressbook-description", "urn:ietf:params:xml:ns:carddav");
                var dispName = resp.GetProperty("displayname", "DAV:");
                var maxResSize = resp.GetProperty("max-resource-size", "urn:ietf:params:xml:ns:carddav");
                var supportedAddrData = resp.GetProperty("supported-address-data", "urn:ietf:params:xml:ns:carddav");

                var addrDataTypes = new List<AddressDataType>();
                if (supportedAddrData != null)
                {
                    foreach (var addrData in supportedAddrData.Elements())
                    {
                        addrDataTypes.Add(new AddressDataType
                        {
                            ContentType = addrData.Attribute("content-type")?.Value,
                            Version = addrData.Attribute("version")?.Value
                        });
                    }
                }

                addressBooks.Add(new AddressBook
                {
                    Path = path,
                    Name = dispName?.Value,
                    Description = desc?.Value,
                    MaxResourceSize = maxResSize != null ? long.Parse(maxResSize.Value) : 0,
                    SupportedAddressData = addrDataTypes
                });
            }

            return addressBooks;
        }

        public async Task<List<AddressObject>> QueryAddressBookAsync(string addressBook, AddressBookQuery query)
        {
            var propReq = await EncodeAddressPropReqAsync(query.DataRequest);
            var addressbookQuery = new AddressBookQueryRequest
            {
                Prop = propReq,
                Filter = new Filter
                {
                    PropFilters = EncodePropFilters(query.PropFilters),
                    Test = query.FilterTest.ToString()
                },
                Limit = query.Limit > 0 ? new Limit { NResults = (uint)query.Limit } : null
            };

            var response = await _webDavClient.ReportAsync(addressBook, addressbookQuery);
            return await DecodeAddressObjectListAsync(response);
        }

        public async Task<List<AddressObject>> MultiGetAddressBookAsync(string path, AddressBookMultiGet multiGet)
        {
            var propReq = await EncodeAddressPropReqAsync(multiGet.DataRequest);
            var addressbookMultiget = new AddressBookMultiGetRequest
            {
                Prop = propReq,
                Hrefs = multiGet.Paths.Count == 0 ? new List<string> { path } : multiGet.Paths
            };

            var response = await _webDavClient.ReportAsync(path, addressbookMultiget);
            return await DecodeAddressObjectListAsync(response);
        }

        public async Task<AddressObject> GetAddressObjectAsync(string path)
        {
            var response = await _webDavClient.GetAsync(path);
            var mediaType = response.Content.Headers.ContentType.MediaType;
            if (!string.Equals(mediaType, "text/vcard", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Expected Content-Type 'text/vcard', got '{mediaType}'");
            }

            var card = await new VCardSerializer().DeserializeAsync(await response.Content.ReadAsStreamAsync());
            var ao = new AddressObject
            {
                Path = response.RequestMessage.RequestUri.AbsolutePath,
                Card = card
            };

            PopulateAddressObject(ao, response.Headers);
            return ao;
        }

        public async Task<AddressObject> PutAddressObjectAsync(string path, VCard card)
        {
            var content = new StringContent(new VCardSerializer().SerializeToString(card));
            content.Headers.ContentType = new MediaTypeHeaderValue("text/vcard");

            var response = await _webDavClient.PutAsync(path, content);
            var ao = new AddressObject { Path = path };
            PopulateAddressObject(ao, response.Headers);
            return ao;
        }

        private async Task<Prop> EncodeAddressPropReqAsync(AddressDataRequest req)
        {
            var addrDataReq = new AddressDataRequestElement
            {
                AllProp = req.AllProp,
                Props = req.Props.Select(p => new Prop { Name = p }).ToList()
            };

            var getLastModReq = new XElement(XName.Get("getlastmodified", "DAV:"));
            var getETagReq = new XElement(XName.Get("getetag", "DAV:"));
            return new Prop
            {
                Elements = new List<XElement> { addrDataReq.ToXElement(), getLastModReq, getETagReq }
            };
        }

        private List<PropFilterElement> EncodePropFilters(List<PropFilter> propFilters)
        {
            return propFilters.Select(pf => new PropFilterElement
            {
                Name = pf.Name,
                Test = pf.Test.ToString(),
                IsNotDefined = pf.IsNotDefined,
                TextMatches = pf.TextMatches.Select(tm => new TextMatchElement
                {
                    Text = tm.Text,
                    NegateCondition = tm.NegateCondition,
                    MatchType = tm.MatchType.ToString()
                }).ToList(),
                Params = pf.Params.Select(p => new ParamFilterElement
                {
                    Name = p.Name,
                    IsNotDefined = p.IsNotDefined,
                    TextMatch = p.TextMatch != null ? new TextMatchElement
                    {
                        Text = p.TextMatch.Text,
                        NegateCondition = p.TextMatch.NegateCondition,
                        MatchType = p.TextMatch.MatchType.ToString()
                    } : null
                }).ToList()
            }).ToList();
        }

        private async Task<List<AddressObject>> DecodeAddressObjectListAsync(MultiStatusResponse response)
        {
            var addrs = new List<AddressObject>();
            foreach (var resp in response.Responses)
            {
                var path = resp.Href;
                var addrData = resp.GetProperty("address-data", "urn:ietf:params:xml:ns:carddav");

                var getLastMod = resp.GetProperty("getlastmodified", "DAV:");
                var getETag = resp.GetProperty("getetag", "DAV:");
                var getContentLength = resp.GetProperty("getcontentlength", "DAV:");

                var data = await new VCardSerializer().DeserializeAsync(new MemoryStream(Convert.FromBase64String(addrData.Value)));

                addrs.Add(new AddressObject
                {
                    Path = path,
                    ModTime = getLastMod != null ? DateTime.Parse(getLastMod.Value) : DateTime.MinValue,
                    ContentLength = getContentLength != null ? long.Parse(getContentLength.Value) : 0,
                    ETag = getETag?.Value,
                    Card = data
                });
            }

            return addrs;
        }

        private void PopulateAddressObject(AddressObject ao, HttpHeaders headers)
        {
            if (headers.Location != null)
            {
                ao.Path = headers.Location.AbsolutePath;
            }
            if (headers.ETag != null)
            {
                ao.ETag = headers.ETag.Tag;
            }
            if (headers.ContentLength.HasValue)
            {
                ao.ContentLength = headers.ContentLength.Value;
            }
            if (headers.LastModified.HasValue)
            {
                ao.ModTime = headers.LastModified.Value.UtcDateTime;
            }
        }
    }
}
