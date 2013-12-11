using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
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
    /// 
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

        private CloudTable table
        {
            get
            {
                CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
                CloudTable table = tableClient.GetTableReference("wwwdavroot");
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
            IEnumerable<Item> items = table.ExecuteQuery(query);
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
            string contentType = string.Empty;
            string rowKey = getRowKey(httpRequestMessage.RequestUri);
            string partitionKey = getPartitionKey(httpRequestMessage.RequestUri, true);
            string blobName = Guid.NewGuid().ToString();
            if (!string.IsNullOrEmpty(httpRequestMessage.Content.Headers.Where(h => h.Key.ToLower() == "content-type").FirstOrDefault().Key))
                contentType = httpRequestMessage.Content.Headers.Where(h => h.Key.ToLower() == "content-type").First().Value.First();
            await httpRequestMessage.Content.ReadAsStreamAsync();
            Stream stream = httpRequestMessage.Content.ReadAsStreamAsync().Result;
            CloudBlockBlob blockBlob = GetBlockBlobReference(blobName);
            blockBlob.UploadFromStream(stream);
            Item item = new Item()
            {
                Created = DateTime.Now,
                Type = 2,
                ContentLength = (int)stream.Length,
                ContentType = contentType,
                RowKey = rowKey,
                PartitionKey = HttpUtility.UrlDecode(partitionKey),
                BlobName = blobName
            };
            TableOperation insertOperation = TableOperation.InsertOrReplace(item);
            table.Execute(insertOperation);
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
            // PropPatch header = If: (<opaquelocktoken:bf680011-4d25-469e-9ff3-1408e1330591>)
            await httpRequestMessage.Content.ReadAsStreamAsync();
            Stream stream = httpRequestMessage.Content.ReadAsStreamAsync().Result;
            XDocument xRequestDocument = XDocument.Load(stream);            
            string owner = xRequestDocument.Descendants(d + "owner").First().Descendants(d + "href").First().Value;
            string lockscope = xRequestDocument.Descendants(d + "lockscope").First().Descendants().First().Name.LocalName;
            string locktype = xRequestDocument.Descendants(d + "locktype").First().Descendants().First().Name.LocalName;
            string locktoken = Guid.NewGuid().ToString();
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
                            new XElement(d + "depth",
                                new XElement(d + "infinity")
                            ),
                            new XElement(d + "locktoken",
                                new XElement(d + "href", "opaquelocktoken:" + locktoken)
                            ),
                            new XElement(d + "timeout", "Second-3600"),
                            new XElement(d + "owner", owner),
                            new XElement(d + "lockroot",
                                new XElement(d + "href", HttpUtility.UrlPathEncode(httpRequestMessage.RequestUri.AbsoluteUri))
                            )
                        )
                    ),
                   new XAttribute(XNamespace.Xmlns + "d", "DAV:")
               )
            );
            xDocument.Declaration = new XDeclaration("1.0", "utf-8", "true");
            HttpResponseMessage httpResponseMessage = new HttpResponseMessage(HttpStatusCode.Created);
            Utf8StringWriter utf8writer = new Utf8StringWriter();
            xDocument.Save(utf8writer);
            StringBuilder sb = utf8writer.GetStringBuilder();
            httpResponseMessage.Content = new StringContent(sb.ToString(), Encoding.UTF8, "application/xml");
            return httpResponseMessage;
        }

        private void recurseDelete(Item item)
        {
            string filter = "PartitionKey eq '" + item.PartitionKey + "&dir;" + item.RowKey + "'";
            TableQuery<Item> query = new TableQuery<Item>().Where(filter);
            foreach (Item entity in table.ExecuteQuery(query))
            {
                if (entity.Type == 2)
                {
                    CloudBlockBlob blockBlob = GetBlockBlobReference(entity.BlobName);
                    blockBlob.Delete(DeleteSnapshotsOption.None);
                }
                TableOperation deleteOperation = TableOperation.Delete(entity);
                TableResult deleteResult = table.Execute(deleteOperation);
                recurseDelete(entity);
            }
        }

        /// <summary>
        /// Delete a resource.
        /// </summary>
        /// <param name="httpRequestMessage">DELETE http request to the url of the resource to delete.</param>
        /// <returns>Http response with a status of 200 OK.</returns>
        [HttpDelete]
        public HttpResponseMessage DELETE(HttpRequestMessage httpRequestMessage)
        {
            string filter;
            string rowKey = getRowKey(httpRequestMessage.RequestUri);
            string partitionKey = getPartitionKey(httpRequestMessage.RequestUri, false);
            string rowParent = getPartitionKey(httpRequestMessage.RequestUri, true);
            filter = "PartitionKey eq '" + partitionKey + "' or " +
                "(RowKey eq '" + rowKey + "' " +
                "and PartitionKey eq '" + rowParent + "')";
            TableQuery<Item> query = new TableQuery<Item>().Where(filter);
            IEnumerable<Item> items = table.ExecuteQuery(query);
            Item item = items.First();
            if (item.Type == 2)
            {
                CloudBlockBlob blockBlob = GetBlockBlobReference(item.BlobName);
                blockBlob.Delete(DeleteSnapshotsOption.None);
            }
            TableOperation deleteOperation = TableOperation.Delete(item);
            TableResult deleteResult = table.Execute(deleteOperation);
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
            string partitionKey = getPartitionKey(httpRequestMessage.RequestUri, false);
            string rowKey = getRowKey(httpRequestMessage.RequestUri);            
            string filter = "PartitionKey eq '" + partitionKey + "' or (RowKey eq '" + rowKey + "')";
            TableQuery<Item> query = new TableQuery<Item>().Where(filter);
            IEnumerable<Item> items = table.ExecuteQuery(query);
            Item item = items.First();
            await httpRequestMessage.Content.ReadAsStreamAsync();
            Stream stream = httpRequestMessage.Content.ReadAsStreamAsync().Result;
            XDocument xDocument = XDocument.Load(stream);
            XNamespace Z = "urn:schemas-microsoft-com:";
            string creationTime = xDocument.Descendants(Z + "Win32CreationTime").First().Value;
            string lastAccessTime = xDocument.Descendants(Z + "Win32LastAccessTime").First().Value;
            string lastModifiedTime = xDocument.Descendants(Z + "Win32LastModifiedTime").First().Value;
            item.Created = DateTime.Parse(creationTime);
            TableOperation tableOperation = TableOperation.InsertOrMerge(item);
            table.Execute(tableOperation);
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
            IEnumerable<Item> items = table.ExecuteQuery(query);
            Item item = items.First();
            TableOperation deleteOperation = TableOperation.Delete(item);
            table.Execute(deleteOperation);
            insertItem = new Item()
            {
                PartitionKey = destinationPartitionKey,
                RowKey = destinationRowKey,
                Type = item.Type,
                BlobName = item.BlobName,
                ContentLength = item.ContentLength,
                ContentType = item.ContentType,
                Created = item.Created
            };            
            TableOperation insertOperation = TableOperation.Insert(insertItem);
            table.Execute(insertOperation);
            if (!string.IsNullOrEmpty(folderFilter))
            {
                query = new TableQuery<Item>().Where(folderFilter);
                items = table.ExecuteQuery(query);
                foreach (Item folderItem in items)
                {
                    deleteOperation = TableOperation.Delete(folderItem);
                    table.Execute(deleteOperation);
                    insertItem = new Item()
                    {
                        PartitionKey = destinationPartitionKey == "wwwdavroot" ? destinationRowKey : destinationPartitionKey + "&dir;" + destinationRowKey,
                        RowKey = folderItem.RowKey,
                        Type = folderItem.Type,
                        BlobName = folderItem.BlobName,
                        ContentLength = folderItem.ContentLength,
                        ContentType = folderItem.ContentType,
                        Created = folderItem.Created
                    };
                    insertOperation = TableOperation.Insert(insertItem);
                    table.Execute(insertOperation);
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
                Type = 1,
                Timestamp = DateTime.Now,
                PartitionKey = partitionKey,
                RowKey = rowKey
            };
            TableOperation insertOperation = TableOperation.Insert(item);
            table.Execute(insertOperation);
            httpResponseMessage.StatusCode = HttpStatusCode.Created;
            return httpResponseMessage;
        }        

        /// <summary>
        /// Return a list of properties for the resource located at the requested url.
        /// </summary>
        /// <param name="httpRequestMessage">PROPFIND http request containing the depth of the request.</param>
        /// <returns>Http response with a status of 207 MultiStatus containing the requested properties.</returns>
        [AcceptVerbs("PROPFIND")]
        public HttpResponseMessage PROPFIND(HttpRequestMessage httpRequestMessage)
        {
            string filter;
            XElement response = null;
            string host = string.Join("//", httpRequestMessage.RequestUri.AbsoluteUri.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Take(2));
            string rowKey = getRowKey(httpRequestMessage.RequestUri);
            string depth = httpRequestMessage.Headers.Where(h => h.Key.ToLower() == "depth").FirstOrDefault().Value.FirstOrDefault();
            string partitionKey = getPartitionKey(httpRequestMessage.RequestUri, false);
            string rowParent = getPartitionKey(httpRequestMessage.RequestUri, true);
            XDocument xDocument = new XDocument(
                new XElement(d + "multistatus",
                    new XAttribute(XNamespace.Xmlns + "d", "DAV:")
                )
            );
            xDocument.Declaration = new XDeclaration("1.0", "utf-8", "true");
            if (partitionKey == "wwwdavroot")
            {
                filter = "PartitionKey eq 'wwwdavroot'";
                if (rowKey != "/")
                {
                    filter = "PartitionKey eq '" + partitionKey + "' and " +
                    "RowKey eq '" + rowKey + "'";
                    TableQuery<Item> query = new TableQuery<Item>().Where(filter);
                    IEnumerable<Item> items = table.ExecuteQuery(query);                    
                        if (items.Count() == 0)
                            return new HttpResponseMessage(HttpStatusCode.NotFound);
                        else
                            response = responseCollection(host + "/" + httpRequestMessage.RequestUri.LocalPath,
                                DateTime.Now.ToString("u").Replace(" ", "T"),
                                rowKey,
                                DateTime.Now.ToString("r"));
                        partitionKey = rowKey;     
                }
                else
                    response = responseCollection(host + "/",
                        DateTime.Now.ToString("u").Replace(" ", "T"),
                        "",
                        DateTime.Now.ToString("r"));
            }
            else
            {                
                filter = "PartitionKey eq '" + partitionKey + "' or " +
                    "(RowKey eq '" + rowKey + "' " +
                    "and PartitionKey eq '" + rowParent + "')";
                TableQuery<Item> query = new TableQuery<Item>().Where(filter);
                IEnumerable<Item> items = table.ExecuteQuery(query);                
                if (items.Count() == 0)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);                
                Item entity = items.First();
                response = responseCollection(host + "/" + httpRequestMessage.RequestUri.LocalPath,
                    entity.Timestamp.ToString("u").Replace(" ", "T"),
                    entity.RowKey,
                    entity.Timestamp.ToString("r"),
                    entity.Type == 1);
            }
            xDocument.Element(d + "multistatus").Add(response);
            if (depth == "1")
            {
                filter = "PartitionKey eq '" + partitionKey + "'";
                TableQuery<Item> query = new TableQuery<Item>().Where(filter);
                IEnumerable<Item> items = table.ExecuteQuery(query);
                foreach (Item entity in items)
                {
                    if (entity.Type == 1)
                    {
                        response = responseCollection(host + "/" + entity.PartitionKey.Replace("&dir;", "/") + "/" + entity.RowKey + "/",
                            entity.Timestamp.ToString("u").Replace(" ", "T"),
                            entity.RowKey,
                            entity.Timestamp.ToString("r"));
                    }
                    else
                    {
                        response = responseCollection(host + "/" + partitionKey.Replace("&dir;", "/") + "/" + HttpUtility.HtmlEncode(entity.RowKey),
                            entity.Timestamp.ToString("u").Replace(" ", "T"),
                            entity.RowKey,
                            entity.Timestamp.ToString("r"),
                            false,
                            entity.ContentLength,
                            entity.ContentType);
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

        private XElement responseCollection(string path, 
            string creationDate, 
            string displayName,             
            string getLastModified,            
            bool collection = true,
            int? getContentLength = null,
            string getContentType = "")
        {
            XElement response = new XElement(d + "response",
                new XElement(d + "href", path),
                new XElement(d + "propstat",
                    new XElement(d + "status", "HTTP/1.1 200 OK"),
                    new XElement(d + "prop",
                        new XElement(d + "creationdate", creationDate),
                        new XElement(d + "displayname", displayName),
                        new XElement(d + "getcontentlength", getContentLength),
                        new XElement(d + "getcontenttype", getContentType),
                        new XElement(d + "getlastmodified", getLastModified),
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
            if (!collection)
                response.Descendants(d + "collection").Remove();
            if(string.IsNullOrEmpty(getContentType))
                response.Descendants(d + "getcontenttype").Remove();
            if(!getContentLength.HasValue)
                response.Descendants(d + "getcontentlength").Remove();
            return response;
        }

        private class Utf8StringWriter : StringWriter
        {
            public override Encoding Encoding { get { return Encoding.UTF8; } }
        }
    }
}