using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WebDAV.Models
{
    public class Prop : TableEntity
    {
        public string Value { get; set; }
    }
}