using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YacaParser.Models
{
    public class DownloaderDb: Targetix.MongoDB.Database.Abstract.DB   
    {
        public DownloaderDb(string connectionString):base(connectionString){
        }
    }
}
