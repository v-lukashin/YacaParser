using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YacaParser.Models
{
    public class StateOptions
    {

        public StateOptions(string l, string c, Action<YandexCatalog>a)
        {
            link = l;
            catalog = c;
            action = a;
        }
        public string link;
        public string catalog;
        public Action<YandexCatalog> action;
    }
}
