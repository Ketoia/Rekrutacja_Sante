using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DbMetaTool
{
    public class Metadata
    {
        public List<DomainMeta> Domains { get; set; } = new();
        public List<TableMeta> Tables { get; set; } = new();
        public List<ProcedureMeta> Procedures { get; set; } = new();
    }

    public class DomainMeta
    {
        public string name { get; set; } = "";
        public string type { get; set; } = "";
        public bool notNull { get; set; } = false;
        public string defaultValue { get; set; } = "";
    }

    public class TableMeta
    {
        public string name { get; set; } = "";
        public List<ColumnMeta> columns { get; set; } = new();
    }

    public class ColumnMeta
    {
        public string name { get; set; } = "";
        public string type { get; set; } = "";
        public bool notNull { get; set; } = false;
        public string defaultValue { get; set; } = "";
    }

    public class ProcedureMeta
    {
        public string name { get; set; } = "";
        public string source { get; set; } = "";
    }
}
