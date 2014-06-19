using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YacaParser.Models
{
    public class CatModel
    {
        public string Uri { get; set; }
        public string Name { get; set; }
        public string Parent { get; set; }
        public string Geo { get; set; }
    }
}
