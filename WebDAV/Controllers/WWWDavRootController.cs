using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Xml;
using System.Xml.Linq;
using WebDAV.Models;

namespace WebDAV.Controllers
{
    /// <summary>
    /// This class responds to WebDAV requests.
    /// </summary>
    public class WWWDavRootController : ApiController
    {
        private string httpMethods = "DELETE, GET, HEAD, LOCK, MOVE, OPTIONS, PROPFIND, PROPPATCH, PUT, UNLOCK";
        private XNamespace d = "DAV:";

        private CloudStorageAccount storageAccount
        {
            get
            {
                return CloudStorageAccount.Parse(CloudConfigurationManager.GetSetting("Microsoft.WindowsAzure.Plugins.Diagnostics.ConnectionString"));
            }
        }

        private CloudTable itemTable
        {
            get
            {
                CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
                CloudTable table = tableClient.GetTableReference("wwwdavroot");
                table.CreateIfNotExists();
                return table;
            }
        }

        private CloudTable lockTable
        {
            get
            {
                CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
                CloudTable table = tableClient.GetTableReference("wwwdavrootLock");
                table.CreateIfNotExists();
                return table;
            }
        }

        private CloudTable propTable
        {
            get
            {
                CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
                CloudTable table = tableClient.GetTableReference("wwwdavrootProp");
                table.CreateIfNotExists();
                return table;
            }
        }

        private CloudBlockBlob GetBlockBlobReference(string BlobName)
        {
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference("wwwdavroot");
            container.CreateIfNotExists();
            return container.GetBlockBlobReference(BlobName);
        }

        private string getRowKey(Uri uri)
        {
            return HttpUtility.UrlDecode(uri.Segments.Last().Replace("'", "''"));
        }

        private string getPartitionKey(Uri uri, bool parent)
        {
            string partitionKey = string.Empty;
            if (uri.Segments.Count() > 2)
            {
                if (parent)
                    partitionKey = string.Join("/", uri.Segments.Skip(1)
                    .Take(uri.Segments.Count() - 2));
                else
                    partitionKey = string.Join("/", uri.Segments.Skip(1));
                partitionKey = HttpUtility.UrlDecode(partitionKey
                    .Replace("//", "&dir;")
                    .Replace("'", "''")
                    .Substring(0, partitionKey.Length - 1));
            }
            else
                partitionKey = "wwwdavroot";
            return partitionKey;
        }

        /// <summary>
        /// Return the resource located at the url.
        /// </summary>
        /// <param name="httpRequestMessage">GET http request to the resource url.</param>
        /// <returns>Http response stream of the resource.</returns>
        [HttpGet]
        public HttpResponseMessage GET(HttpRequestMessage httpRequestMessage)
        {
            HttpResponseMessage httpResponseMessage = new HttpResponseMessage();
            httpResponseMessage.StatusCode = HttpStatusCode.OK;
            MemoryStream stream = new MemoryStream();
            string rowKey = getRowKey(httpRequestMessage.RequestUri);
            string partitionKey = getPartitionKey(httpRequestMessage.RequestUri, true);
            string filter = "PartitionKey eq 'wwwdavroot'";
            if (rowKey != "/")
            {
                filter = "PartitionKey eq '" + partitionKey + "' and " +
                "RowKey eq '" + rowKey + "'";
            }
            TableQuery<Item> query = new TableQuery<Item>().Where(filter);
            IEnumerable<Item> items = itemTable.ExecuteQuery(query);
            if (items.Count() > 0)
            {
                Item item = items.First();
                if (!string.IsNullOrEmpty(item.BlobName))
                {
                    CloudBlockBlob blockBlob = GetBlockBlobReference(item.BlobName);
                    blockBlob.DownloadToStream(stream);
                    stream.Position = 0;
                    httpResponseMessage.Content = new StreamContent(stream);
                    if (!string.IsNullOrEmpty(item.ContentType))
                        httpResponseMessage.Content.Headers.Add("content-type", item.ContentType);
                    httpResponseMessage.Content.Headers.Add("content-length", item.ContentLength.ToString());
                    httpResponseMessage.StatusCode = HttpStatusCode.OK;
                }
                else
                    httpResponseMessage.StatusCode = HttpStatusCode.InternalServerError;
            }
            return httpResponseMessage;
        }

        /// <summary>
        /// Upload a new resource.
        /// </summary>
        /// <param name="httpRequestMessage">PUT http request containing the resource to the target url.</param>
        /// <returns>Http response with a status of 201 Created.</returns>
        [HttpPut]
        public async Task<HttpResponseMessage> PUT(HttpRequestMessage httpRequestMessage)
        {
            //string contentType = string.Empty;
            string blobName = Guid.NewGuid().ToString();
            CloudBlockBlob blockBlob = null;
            Item item = null;
            Stream stream = httpRequestMessage.Content.ReadAsStreamAsync().Result;
            string rowKey = getRowKey(httpRequestMessage.RequestUri);
            string partitionKey = getPartitionKey(httpRequestMessage.RequestUri, true);
            //if (!string.IsNullOrEmpty(httpRequestMessage.Content.Headers.Where(h => h.Key.ToLower() == "content-type").FirstOrDefault().Key))
            //    contentType = httpRequestMessage.Content.Headers.Where(h => h.Key.ToLower() == "content-type").First().Value.First();
            //if (string.IsNullOrEmpty(contentType))
            string contentType = MimeMapping.GetMimeMapping(rowKey);
            string filter = "PartitionKey eq 'wwwdavroot'";
            if (rowKey != "/")
            {
                filter = "PartitionKey eq '" + partitionKey + "' and " +
                "RowKey eq '" + rowKey + "'";
            }
            TableQuery<Item> query = new TableQuery<Item>().Where(filter);
            IEnumerable<Item> items = itemTable.ExecuteQuery(query);
            if (items.Count() > 0)
            {
                item = items.First();
                item.Timestamp = DateTime.Now;
                item.ContentLength = (int)stream.Length;
                item.ContentType = contentType;
                blockBlob = GetBlockBlobReference(item.BlobName);
            }
            else
            {
                item = new Item()
                {
                    Created = DateTime.Now,
                    Collection = false,
                    ContentLength = (int)stream.Length,
                    ContentType = contentType,
                    RowKey = rowKey,
                    PartitionKey = partitionKey,
                    BlobName = blobName
                };
                blockBlob = GetBlockBlobReference(blobName);
            }
            blockBlob.UploadFromStream(stream);
            TableOperation insertOperation = TableOperation.InsertOrReplace(item);
            itemTable.Execute(insertOperation);
            return new HttpResponseMessage(HttpStatusCode.Created);
        }

        /// <summary>
        /// Unlock a resource.
        /// </summary>
        /// <param name="httpRequestMessage">UNLOCK http request - TBC</param>
        /// <returns>Empty http response message.</returns>
        [AcceptVerbs("UNLOCK")]
        public HttpResponseMessage UNLOCK(HttpRequestMessage httpRequestMessage)
        {
            Item item = getItem(httpRequestMessage);
            string lockToken = httpRequestMessage.Headers
                .Where(h => h.Key == "Lock-Token").FirstOrDefault()
                .Value.FirstOrDefault()
                .Replace("<", "").Replace(">", "");
            if (!string.IsNullOrEmpty(lockToken))
            {
                string filter = "RowKey eq '" + lockToken + "'";
                TableQuery<Lock> query = new TableQuery<Lock>().Where(filter);
                IEnumerable<Lock> locks = lockTable.ExecuteQuery(query);
                Lock lockItem = locks.First();
                TableOperation deleteOperation = TableOperation.Delete(lockItem);
                TableResult deleteResult = lockTable.Execute(deleteOperation);
            }
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        /// <summary>
        /// Lock a resource.
        /// </summary>
        /// <param name="httpRequestMessage">LOCK http request containing the lock type, scope & owner.</param>
        /// <returns>Http response with a status of 201 Created containing information about the lock.</returns>
        [AcceptVerbs("LOCK")]
        public async Task<HttpResponseMessage> LOCK(HttpRequestMessage httpRequestMessage)
        {
            string owner = string.Empty, 
                lockscope = string.Empty, 
                locktype = string.Empty, 
                locktoken = string.Empty;
            Item item = getItem(httpRequestMessage);
            string timeout = "Second-3600";
            HttpResponseMessage httpResponseMessage = new HttpResponseMessage();
            KeyValuePair<string, IEnumerable<string>> timeoutHeader = httpRequestMessage.Headers
                .Where(h => h.Key == "Timeout").FirstOrDefault();
            if (!string.IsNullOrEmpty(timeoutHeader.Value.FirstOrDefault()))
                timeout = timeoutHeader.Value.FirstOrDefault();
            Stream stream = httpRequestMessage.Content.ReadAsStreamAsync().Result;
            if (stream.Length > 0) //New Lock
            {
                httpResponseMessage.StatusCode = HttpStatusCode.Created;
                XDocument xRequestDocument = XDocument.Load(stream);
                owner = xRequestDocument.Descendants(d + "owner").First().Descendants(d + "href").First().Value;
                lockscope = xRequestDocument.Descendants(d + "lockscope").First().Descendants().First().Name.LocalName;
                locktype = xRequestDocument.Descendants(d + "locktype").First().Descendants().First().Name.LocalName;
                locktoken = "opaquelocktoken:" + Guid.NewGuid().ToString();
            }
            else // Renew existing lock
            {
                httpResponseMessage.StatusCode = HttpStatusCode.OK;
                KeyValuePair<string, IEnumerable<string>> ifHeader = httpRequestMessage.Headers
                .Where(h => h.Key == "If").FirstOrDefault();
                if(!string.IsNullOrEmpty(ifHeader.Value.FirstOrDefault()))
                    locktoken = ifHeader.Value.First().Substring(2, ifHeader.Value.First().Length - 4);
                string filter = "RowKey eq '" + locktoken + "'";
                TableQuery<Lock> query = new TableQuery<Lock>().Where(filter);
                IEnumerable<Lock> locks = lockTable.ExecuteQuery(query);
                Lock lockItem = locks.First();
                lockscope = lockItem.Scope;
                locktype = lockItem.Type;
                owner = lockItem.Owner;
            }
            Lock itemLock = new Lock()
            {
                PartitionKey = item.BlobName,
                RowKey = locktoken,
                Expires = DateTime.Now.AddSeconds(int.Parse(timeout.Substring(timeout.IndexOf("-") + 1))),
                Scope = lockscope,
                Type = locktype,
                Owner = owner
            };
            TableOperation insertOperation = TableOperation.InsertOrReplace(itemLock);
            lockTable.Execute(insertOperation);
            XDocument xDocument = new XDocument(
               new XElement(d + "prop",
                   new XElement(d + "lockdiscovery",
                       new XElement(d + "activelock",
                           new XElement(d + "locktype",
                               new XElement(d + locktype)
                            ),
                            new XElement(d + "lockscope",
                                new XElement(d + lockscope)
                            ),
                            new XElement(d + "depth", "infinity" //0
                            ),
                            new XElement(d + "locktoken",
                                new XElement(d + "href", locktoken)
                            ),
                            new XElement(d + "timeout", timeout),
                            new XElement(d + "owner", owner),
                            new XElement(d + "lockroot",
                                new XElement(d + "href", Host(httpRequestMessage) + httpRequestMessage.RequestUri.LocalPath)
                            )
                        )
                    ),
                   new XAttribute(XNamespace.Xmlns + "d", "DAV:")
               )
            );
            xDocument.Declaration = new XDeclaration("1.0", "utf-8", "true");
            Utf8StringWriter utf8writer = new Utf8StringWriter();
            xDocument.Save(utf8writer);
            StringBuilder sb = utf8writer.GetStringBuilder();
            httpResponseMessage.Content = new StringContent(sb.ToString(), Encoding.UTF8, "application/xml");
            httpResponseMessage.Content.Headers.Add("Lock-Token", "<" + locktoken + ">");
            return httpResponseMessage;
        }

        private void recurseDelete(Item item)
        {
            string filter = "PartitionKey eq '" + item.PartitionKey + "&dir;" + item.RowKey + "'";
            TableQuery<Item> query = new TableQuery<Item>().Where(filter);
            foreach (Item entity in itemTable.ExecuteQuery(query))
            {
                if (!entity.Collection)
                {
                    CloudBlockBlob blockBlob = GetBlockBlobReference(entity.BlobName);
                    blockBlob.Delete(DeleteSnapshotsOption.None);
                }
                TableOperation deleteOperation = TableOperation.Delete(entity);
                TableResult deleteResult = itemTable.Execute(deleteOperation);
                recurseDelete(entity);
            }
        }

        [HttpHead]
        public HttpResponseMessage HEAD(HttpRequestMessage httpRequestMessage)
        {
            Item item = getItem(httpRequestMessage);
            HttpResponseMessage httpResponseMessage = new HttpResponseMessage();
            httpResponseMessage.StatusCode = HttpStatusCode.OK;
            httpResponseMessage.Content = new StringContent("");
            if (item != null && !string.IsNullOrEmpty(item.ContentType))
                httpResponseMessage.Content.Headers.ContentType = new MediaTypeHeaderValue(item.ContentType);
            else
                httpResponseMessage.StatusCode = HttpStatusCode.NotFound;
            return httpResponseMessage;
        }

        private Item getItem(HttpRequestMessage httpRequestMessage)
        {
            string filter;
            string rowKey = getRowKey(httpRequestMessage.RequestUri);
            string partitionKey = getPartitionKey(httpRequestMessage.RequestUri, false);
            string rowParent = getPartitionKey(httpRequestMessage.RequestUri, true);
            filter = "RowKey eq '" + rowKey + "' " +
                "and PartitionKey eq '" + rowParent + "'";
            TableQuery<Item> query = new TableQuery<Item>().Where(filter);
            IEnumerable<Item> items = itemTable.ExecuteQuery(query);
            if (items.Count() > 0)
                return items.First();
            else
                return null;
        }

        /// <summary>
        /// Delete a resource.
        /// </summary>
        /// <param name="httpRequestMessage">DELETE http request to the url of the resource to delete.</param>
        /// <returns>Http response with a status of 200 OK.</returns>
        [HttpDelete]
        public HttpResponseMessage DELETE(HttpRequestMessage httpRequestMessage)
        {
            Item item = getItem(httpRequestMessage);
            if (!item.Collection)
            {
                CloudBlockBlob blockBlob = GetBlockBlobReference(item.BlobName);
                blockBlob.Delete(DeleteSnapshotsOption.None);
            }
            TableOperation deleteOperation = TableOperation.Delete(item);
            TableResult deleteResult = itemTable.Execute(deleteOperation);
            recurseDelete(item);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        /// <summary>
        /// List the permitted methods, headers, DAV versions and cross site scripting options.
        /// </summary>
        /// <returns>Empty http response with headers describing the permitted options.</returns>
        [HttpOptions]
        public HttpResponseMessage OPTIONS()
        {
            HttpResponseMessage httpResponseMessage = new HttpResponseMessage(HttpStatusCode.OK);
            httpResponseMessage.Content = new StringContent("");
            httpResponseMessage.Headers.Add("DAV", "1, 2");
            httpResponseMessage.Headers.Add("MS-Author-Via", "DAV");
            httpResponseMessage.Content = new StringContent("");
            httpResponseMessage.Content.Headers.TryAddWithoutValidation("Allow", httpMethods);
            httpResponseMessage.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            return httpResponseMessage;
        }

        /// <summary>
        /// Update the properties of an existing resource.
        /// </summary>
        /// <param name="httpRequestMessage">PROPPATCH http request containing the properties to update.</param>
        /// <returns>Http response with a status of 200 OK.</returns>
        [AcceptVerbs("PROPPATCH")]
        public async Task<HttpResponseMessage> PROPPATCH(HttpRequestMessage httpRequestMessage)
        {
            Prop prop = null;
            Item item = getItem(httpRequestMessage);            
            Stream stream = httpRequestMessage.Content.ReadAsStreamAsync().Result;
            XDocument xDocument = XDocument.Load(stream);
            foreach(XElement xElement in xDocument.Descendants(d + "prop").Descendants())
            {
                prop = new Prop(){ 
                    PartitionKey = item.BlobName,
                    RowKey = xElement.Name.NamespaceName + xElement.Name.LocalName,
                    Value = xElement.Value
                };
                TableOperation tableOperation = TableOperation.InsertOrMerge(prop);
                propTable.Execute(tableOperation);
            }                        
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        /// <summary>
        /// Moves a resource to a new location.
        /// </summary>
        /// <param name="httpRequestMessage">MOVE http request containing the destination.</param>
        /// <returns>Http response with a status of 200 OK.</returns>
        [AcceptVerbs("MOVE")]
        public HttpResponseMessage MOVE(HttpRequestMessage httpRequestMessage)
        {
            HttpResponseMessage httpResponseMessage = new HttpResponseMessage();
            Item insertItem;
            string itemFilter, folderFilter = string.Empty;
            string rowKey = getRowKey(httpRequestMessage.RequestUri);
            string partitionKey = getPartitionKey(httpRequestMessage.RequestUri, false);
            string rowParent = getPartitionKey(httpRequestMessage.RequestUri, true);
            string destinationHeader = httpRequestMessage.Headers.Where(h => h.Key == "Destination").FirstOrDefault().Value.FirstOrDefault();
            string destination = destinationHeader.Substring(destinationHeader.LastIndexOf("/") + 1);
            Uri uriDestination = new Uri(destinationHeader);
            string destinationRowKey = getRowKey(uriDestination);
            string destinationPartitionKey = getPartitionKey(uriDestination, true);
            if (partitionKey == "wwwdavroot")
            {
                itemFilter = "PartitionKey eq 'wwwdavroot'";
                if (rowKey != "/")
                {
                    itemFilter = "PartitionKey eq '" + partitionKey + "' and " +
                    "RowKey eq '" + rowKey + "'";
                }
            }
            else
            {
                itemFilter = "(RowKey eq '" + rowKey + "' " +
                    "and PartitionKey eq '" + rowParent + "')";
                folderFilter = "PartitionKey eq '" + partitionKey + "'";
            }
            TableQuery<Item> query = new TableQuery<Item>().Where(itemFilter);
            IEnumerable<Item> items = itemTable.ExecuteQuery(query);
            Item item = items.First();
            TableOperation deleteOperation = TableOperation.Delete(item);
            itemTable.Execute(deleteOperation);
            insertItem = new Item()
            {
                PartitionKey = destinationPartitionKey,
                RowKey = destinationRowKey,
                Collection = item.Collection,
                BlobName = item.BlobName,
                ContentLength = item.ContentLength,
                ContentType = item.ContentType,
                Created = item.Created
            };
            TableOperation insertOperation = TableOperation.Insert(insertItem);
            itemTable.Execute(insertOperation);
            if (!string.IsNullOrEmpty(folderFilter))
            {
                query = new TableQuery<Item>().Where(folderFilter);
                items = itemTable.ExecuteQuery(query);
                foreach (Item folderItem in items)
                {
                    deleteOperation = TableOperation.Delete(folderItem);
                    itemTable.Execute(deleteOperation);
                    insertItem = new Item()
                    {
                        PartitionKey = destinationPartitionKey == "wwwdavroot" ? destinationRowKey : destinationPartitionKey + "&dir;" + destinationRowKey,
                        RowKey = folderItem.RowKey,
                        Collection = folderItem.Collection,
                        BlobName = folderItem.BlobName,
                        ContentLength = folderItem.ContentLength,
                        ContentType = folderItem.ContentType,
                        Created = folderItem.Created
                    };
                    insertOperation = TableOperation.Insert(insertItem);
                    itemTable.Execute(insertOperation);
                }
            }

            httpResponseMessage.StatusCode = HttpStatusCode.OK;
            return httpResponseMessage;
        }

        /// <summary>
        /// Create a new collection
        /// </summary>
        /// <param name="httpRequestMessage">MKCOL http request to the url of the new collection.</param>
        /// <returns>Http response with a status of 201 Created.</returns>
        [AcceptVerbs("MKCOL")]
        public HttpResponseMessage MKCOL(HttpRequestMessage httpRequestMessage)
        {
            string partitionKey = getPartitionKey(httpRequestMessage.RequestUri, true);
            string rowKey = getRowKey(httpRequestMessage.RequestUri);
            HttpResponseMessage httpResponseMessage = new HttpResponseMessage();
            Item item = new Item()
            {
                ContentType = "",
                Created = DateTime.Now,
                Collection = true,
                Timestamp = DateTime.Now,
                PartitionKey = partitionKey,
                RowKey = rowKey
            };
            TableOperation insertOperation = TableOperation.Insert(item);
            itemTable.Execute(insertOperation);
            httpResponseMessage.StatusCode = HttpStatusCode.Created;
            return httpResponseMessage;
        }

        private string Host(HttpRequestMessage httpRequestMessage)
        {
            return httpRequestMessage.RequestUri.AbsoluteUri.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries).First() + "//" + httpRequestMessage.Headers.Host;
        }

        /// <summary>
        /// Return a list of properties for the resource located at the requested url.
        /// </summary>
        /// <param name="httpRequestMessage">PROPFIND http request containing the depth of the request.</param>
        /// <returns>Http response with a status of 207 MultiStatus containing the requested properties.</returns>
        [AcceptVerbs("PROPFIND")]
        public async Task<HttpResponseMessage> PROPFIND(HttpRequestMessage httpRequestMessage)
        {
            string filter = string.Empty;
            Item entity = null;
            XElement response = null;
            XDocument xRequest = null;
            XDocument xDocument = new XDocument(
                new XElement(d + "multistatus",
                    new XAttribute(XNamespace.Xmlns + "d", "DAV:")
                )
            );
            xDocument.Declaration = new XDeclaration("1.0", "utf-8", "true");
            Stream stream = httpRequestMessage.Content.ReadAsStreamAsync().Result;
            string rowKey = getRowKey(httpRequestMessage.RequestUri);
            string depth = httpRequestMessage.Headers.Where(h => h.Key.ToLower() == "depth").FirstOrDefault().Value.FirstOrDefault();
            string partitionKey = getPartitionKey(httpRequestMessage.RequestUri, false);
            string rowParent = getPartitionKey(httpRequestMessage.RequestUri, true);
            if (partitionKey == "wwwdavroot")
            {
                filter = "PartitionKey eq 'wwwdavroot'";
                if (rowKey != "/")
                {
                    filter = "PartitionKey eq '" + partitionKey + "' and " +
                    "RowKey eq '" + rowKey + "'";
                    TableQuery<Item> query = new TableQuery<Item>().Where(filter);
                    IEnumerable<Item> items = itemTable.ExecuteQuery(query);
                    if (items.Count() == 0)
                        return new HttpResponseMessage(HttpStatusCode.NotFound);
                    else
                    {
                        entity = items.First();
                        response = responseCollection(Host(httpRequestMessage) + httpRequestMessage.RequestUri.LocalPath, entity);
                    }
                    partitionKey = rowKey;
                }
                else
                    response = responseCollection(Host(httpRequestMessage),
                        new Item()
                        {
                            Created = DateTime.Now,
                            RowKey = "",
                            Timestamp = DateTime.Now,
                            Collection = true
                        });
            }
            else
            {
                filter = "PartitionKey eq '" + partitionKey + "' or " +
                    "(RowKey eq '" + rowKey + "' " +
                    "and PartitionKey eq '" + rowParent + "')";
                TableQuery<Item> query = new TableQuery<Item>().Where(filter);
                IEnumerable<Item> items = itemTable.ExecuteQuery(query);
                if (items.Count() == 0)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                entity = items.First();
                response = responseCollection(Host(httpRequestMessage) + httpRequestMessage.RequestUri.LocalPath, entity);
            }
            if (stream.Length > 0)            
                xRequest = XDocument.Load(stream);
            if(stream.Length > 0 && xRequest.Descendants(d + "allprop").Count() == 0)
            {
                response = new XElement(d + "response",
                    new XElement(d + "href", Host(httpRequestMessage) + httpRequestMessage.RequestUri.LocalPath),
                    new XElement(d + "propstat",
                        new XElement(d + "status", "HTTP/1.1 200 OK"),
                        new XElement(d + "prop")
                    ),
                    new XElement(d + "propstat",
                        new XElement(d + "status", "HTTP/1.1 404 Not Found"),
                        new XElement(d + "prop")
                    )
                );
                foreach (var xElement in xRequest.Descendants(d + "prop").Descendants())
                {
                    filter = "PartitionKey eq '" + entity.BlobName + "' and RowKey eq '" + xElement.Name.NamespaceName + xElement.Name.LocalName + "'";
                    TableQuery<Prop> query = new TableQuery<Prop>().Where(filter);
                    IEnumerable<Prop> props = propTable.ExecuteQuery(query);
                    if (props.Count() > 0)
                        response.Descendants(d + "prop").First().Add(
                                new XElement(xElement.Name.NamespaceName + xElement.Name.LocalName, xElement.Value)
                            );
                    else
                         response.Descendants(d + "prop").Last().Add(
                                new XElement(xElement.Name.Namespace + xElement.Name.LocalName)
                            );                                    
                }
            }
            xDocument.Element(d + "multistatus").Add(response);
            if (depth == "1")
            {
                filter = "PartitionKey eq '" + partitionKey + "'";
                TableQuery<Item> query = new TableQuery<Item>().Where(filter);
                IEnumerable<Item> items = itemTable.ExecuteQuery(query);
                if (partitionKey == "wwwdavroot")
                    partitionKey = "";
                foreach (Item item in items)
                {
                    if (item.Collection)
                    {
                        response = responseCollection(Host(httpRequestMessage) + item.PartitionKey.Replace("&dir;", "/") + "/" + item.RowKey + "/", item);
                    }
                    else
                    {
                        response = responseCollection(Host(httpRequestMessage) + partitionKey.Replace("&dir;", "/") + "/" + HttpUtility.HtmlEncode(item.RowKey), item);
                    }
                    xDocument.Element(d + "multistatus").Add(response);
                }
            }
            HttpResponseMessage httpResponseMessage = new HttpResponseMessage((HttpStatusCode)207);
            Utf8StringWriter utf8writer = new Utf8StringWriter();
            xDocument.Save(utf8writer);
            StringBuilder sb = utf8writer.GetStringBuilder();
            httpResponseMessage.Content = new StringContent(sb.ToString(), Encoding.UTF8, "application/xml");
            return httpResponseMessage;
        }

        private XElement lockDiscovery(Item item)
        {
            XElement xElement = null;
            string filter = "PartitionKey eq '" + item.BlobName + "'";
            TableQuery<Lock> query = new TableQuery<Lock>().Where(filter);
            IEnumerable<Lock> items = lockTable.ExecuteQuery(query);
            if (items.Count() > 0)
            {
                XElement lockDiscovery = new XElement(d + "lockdiscovery");
                foreach (Lock lockItem in items)
                {
                    xElement = new XElement(d + "lockdiscovery",
                        new XElement(d + "activelock",
                            new XElement(d + "locktype",
                                new XElement(d + lockItem.Type, "")
                            ),
                            new XElement(d + "lockscope",
                                new XElement(d + lockItem.Scope, "")
                            ),
                            new XElement(d + "depth", "0"),
                            new XElement(d + "locktoken",
                                new XElement(d + "href", lockItem.RowKey)
                            ),
                            new XElement(d + "timeout", "Second-" +
                                Math.Round((lockItem.Expires - DateTime.Now).TotalSeconds).ToString()
                            ),
                            new XElement(d + "owner", lockItem.Owner)
                        )
                    );
                    lockDiscovery.Add(xElement);
                }
            }
            return xElement;
        }

        private XElement responseCollection(string path, Item item)
        {
            XElement response = new XElement(d + "response",
                new XElement(d + "href", path),
                new XElement(d + "propstat",
                    new XElement(d + "status", "HTTP/1.1 200 OK"),
                    new XElement(d + "prop",
                        new XElement(d + "creationdate", item.Created.ToString("u").Replace(" ", "T")),
                        new XElement(d + "displayname", item.RowKey),
                        new XElement(d + "getcontentlength", item.ContentLength.ToString()),
                        new XElement(d + "getcontenttype", item.ContentType),
                        new XElement(d + "getlastmodified", item.Timestamp.ToString("r")),
                        new XElement(d + "resourcetype",
                            new XElement(d + "collection")
                        ),
                        new XElement(d + "supportedlock",
                            new XElement(d + "lockentry",
                                new XElement(d + "lockscope",
                                    new XElement(d + "exclusive")
                                ),
                                new XElement(d + "locktype",
                                    new XElement(d + "write")
                                )
                            ),
                            new XElement(d + "lockentry",
                                new XElement(d + "lockscope",
                                    new XElement(d + "shared")
                                ),
                                new XElement(d + "locktype",
                                    new XElement(d + "write")
                                )
                            )
                        )
                    )
                )
            );
            if (!item.Collection)
            {
                response.Descendants(d + "collection").Remove();
                response.Descendants(d + "prop").First().Add(lockDiscovery(item));
            }
            if (string.IsNullOrEmpty(item.ContentType))
                response.Descendants(d + "getcontenttype").Remove();
            if (item.ContentLength == 0)
                response.Descendants(d + "getcontentlength").Remove();           
            return response;
        }

        private class Utf8StringWriter : StringWriter
        {
            public override Encoding Encoding { get { return Encoding.UTF8; } }
        }
    }
}