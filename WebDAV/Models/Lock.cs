using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WebDAV.Models
{
    public class Lock : TableEntity
    {        
        public string Scope { get; set; }
        public string Type { get; set; }
        public DateTime Expires { get; set; }
        public string Owner { get; set; }
    }
}