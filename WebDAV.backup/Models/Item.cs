using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WebDAV.Models
{
    public class Item : TableEntity
    {
        public Item(string PartitionKey, string RowKey)
        {
            this.PartitionKey = PartitionKey;
            this.RowKey = RowKey;
        }

        public Item() { }

        public int Type { get; set; }
        public DateTime Created { get; set; }
        public string ContentType { get; set; }
        public int ContentLength { get; set; }
        public string BlobName { get; set; }       
    }
}