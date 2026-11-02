using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace enfold2minerva.Model
{
    public class LinkedFile
    {
        public string SourceUrl { get; set; } = "";
        public string LocalPath { get; set; } = "";
        public string Sha256 { get; set; } = "";
    }
}