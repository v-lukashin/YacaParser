using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Targetix.Helpers.Security;
using Targetix.MongoDB.Extensions.Model;

namespace YacaParser.Models
{
    public class YandexCatalog: ICustomKeyDbModel
    {
        private string _id;
        private HashSet<string> _geo;
        public string Uri { get; set; }

        [BsonIgnoreIfNull]
        public string Catalog { get; set; }

        [BsonIgnoreIfNull]
        public string Parent { get; set; }

        [BsonIgnoreIfNull]
        public string Description { get; set; }

        [BsonIgnoreIfNull]
        public HashSet<string> Geo
        {
            get
            {
                if (_geo == null) _geo = new HashSet<string>();
                return _geo;
            }
            set { _geo = value; }
        }

        [BsonIgnoreIfNull]
        public bool IsSchool { get; set; }

        [BsonIgnoreIfNull]
        public bool IsBlog { get; set; }

        [BsonIgnoreIfNull]
        public bool IsForum { get; set; }

        [BsonIgnoreIfNull]
        public bool IsAdvice { get; set; }

        [BsonIgnoreIfNull]
        public bool IsEvent { get; set; }

        [BsonIgnoreIfNull]
        public bool IsEncycl { get; set; }

        [BsonIgnoreIfNull]
        public bool IsService { get; set; }

        public string Id
        {
            get
            {
                _id = _id ?? HashGenerator.Md5Hash(new Uri(Uri).GetShortUrl());
                return _id;
            }
            set
            {
                _id = value;
            }
        }
    }
}
