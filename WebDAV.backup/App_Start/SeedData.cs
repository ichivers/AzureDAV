using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;

namespace WebDAV
{
    public class SeedData
    {
        public static void Seed()
        {
            Directory.Exists(@"\\localhost@55335\DavWWWRoot");
        }
    }
}