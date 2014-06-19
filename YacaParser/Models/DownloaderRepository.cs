using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Targetix.MongoDB.Extensions.Model;

namespace YacaParser.Models
{
    public class DownloaderRepository : Targetix.Repository.MongoShardKeyRepository<YandexCatalog>
    {
        public DownloaderRepository(Targetix.MongoDB.Database.Abstract.DB db) : base(db) { }
    }
}
