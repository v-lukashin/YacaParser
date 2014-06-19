using System;
using System.Collections;
using System.Linq;
using Targetix.AdMira;
using System.Diagnostics;
using System.Collections.Generic;
using Targetix.Repository;
using Targetix.Model.Visitors;
using Targetix.MongoDB.Database;
using Targetix.Model.VisitorActions;
using Targetix.Model.Index;
using Targetix.Helpers.Security;
using Targetix.Model.AudienceBuilderCache;
using MongoDB.Bson;
using Targetix.Couchbase.Database;
using Targetix.Model;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using YacaParser.Models;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;

namespace YacaParser
{
    class Program
    {
        static void Main(string[] args)
        {
            //MergeGeneralWithBlogAndSchool();
            //TestReadSave();
            //TestDownloader();
            //TestGeo();
            //TestGetAll();
            //TestMemoryListTVI();
            ParseYaca();
            //CreateAudience();
            //CreateBuilderCache();
            //TestMemory();
            //fill();
        }
        

        static void MergeGeneralWithBlogAndSchool()
        {
            DownloaderRepository rep = new DownloaderRepository(new DownloaderDb("mongodb://localhost:27017/YandexCatalogOtherTest"));
            DownloaderRepository repRes = new DownloaderRepository(new DownloaderDb("mongodb://localhost:27017/YandexCatalogCompiled"));

            IEnumerable<YandexCatalog> sb = rep.GetAll();
            foreach (var y in sb)
            {
                YandexCatalog cat = repRes.GetById(y.Id) ?? new YandexCatalog { Id = y.Id, Uri = y.Uri, Description = y.Description};
                cat.IsBlog = y.IsBlog;
                cat.IsSchool = y.IsSchool;
                repRes.Save(cat);
            }
        }

        static void TestReadSave()
        {
            Queue<CatModel> queue = new Queue<CatModel>();
            queue.Enqueue(new CatModel { Geo = "Rus", Name = "Envir" , Parent = "ROOT", Uri = "http://bla.bla"});
            SaveQueue(queue);
            queue = null;
            ReadQueue(out queue);
        }

        static void SaveQueue(Queue<CatModel> queue)
        {
            using (FileStream fs = new FileStream("State.txt", FileMode.Create))
            {
                string jsonStr = JsonConvert.SerializeObject(queue);
                byte[] res = System.Text.Encoding.UTF8.GetBytes(jsonStr);
                fs.Write(res, 0, res.Length);
            }
        }

        static void ReadQueue(out Queue<CatModel> queue)
        {
            using (FileStream fs = new FileStream("State.txt", FileMode.Open))
            {
                byte[] byteArr = new byte[fs.Length];
                fs.Read(byteArr, 0, byteArr.Length);
                Queue<CatModel> res = JsonConvert.DeserializeObject<Queue<CatModel>>(System.Text.Encoding.UTF8.GetString(byteArr));
                queue = res;
            }
        }


        static void TestDownloader()
        {
            ThreadPool.SetMaxThreads(5, 5);

            Task tsk = new Task(printAval);
            tsk.Start();

            string uri = "http://yaca.yandex.ru/yca/cat/Entertainment/";
            Stopwatch sw = Stopwatch.StartNew();
            Parallel.For(0, 1000, i =>
            {
                string res = Download(uri);
                if (res == null)
                {
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.WriteLine("\t\t\tres[{0}]", i);
                    Console.ForegroundColor = ConsoleColor.Gray;
                }
            });
            sw.Stop();
            Console.WriteLine("Main time {0}", sw.Elapsed.TotalSeconds);
            Thread.Sleep(2000);
        }

        static void printAval()
        {
            int a, s;
            while (true)
            {
                ThreadPool.GetAvailableThreads(out a, out s);
                Console.WriteLine("a={0}, s={1}", a, s);
                Thread.Sleep(2000);
            }
        }
        static public string Download(string link)
        {
            WebClient cli = new WebClient();
            cli.BaseAddress = "http://yaca.yandex.ru";
            cli.Proxy = null;
            cli.Encoding = Encoding.UTF8;
            Stopwatch sw = Stopwatch.StartNew();

            string page = null;
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    page = cli.DownloadString(link);
                    break;
                }
                catch (WebException wexc)
                {
                    ConsoleColor col = ConsoleColor.Gray;
                    switch (i)
                    {
                        case 1: col = ConsoleColor.Green; break;
                        case 2: col = ConsoleColor.Yellow; break;
                        case 3: col = ConsoleColor.Red; break;
                        case 4: col = ConsoleColor.DarkRed; break;
                    }
                    Console.ForegroundColor = col;
                    Console.WriteLine("TimeoutError({0}). Repeat", i);
                    continue;
                }
            }
            sw.Stop();
            double time = sw.Elapsed.TotalSeconds;
            ConsoleColor color = ConsoleColor.Gray;
            if (time > 10)
            {
                if (time > 30)
                {
                    if (time > 50)
                    {
                        color = ConsoleColor.Red;
                    }
                    else color = ConsoleColor.Yellow;
                }
                else color = ConsoleColor.Green;
            }
            Console.ForegroundColor = color;
            Console.WriteLine("\t\t\t\t\t\tTime download {0}", time);
            Console.ForegroundColor = ConsoleColor.Gray;
            return page;
        }


        static Action<YandexCatalog> getAction(string name)
        {
            switch (name)
            {
                case "Товары и услуги": return y => y.IsService = true;
                case "Советы": return y => y.IsAdvice = true;
                case "Энциклопедии": return y => y.IsEncycl = true;
                case "Форумы": return y => y.IsForum = true;
                case "Мероприятия": return y => y.IsEvent = true;
                default: return null;
            }
        }
        static void TestGeo()
        {
            string pattern = @"<a href=""(?<uri>[-\w/]*)"" class=""b-rubric__list__item__link"">(?<name>[-\w ]*)</a>";
            string patternGeoDropdown = @"<a href=""(?<uri>[-\w/]*)"" class=""b-dropdown__link"">(?<name>[-\w ]*)</a>";
            string patternCountSites = @"<h2 class=""b-site-counter__number"">(?<cnt>\d*)[ \w]*</h2>";
            string patternAdditonal = @"<a href=""(?<uri>[-\w/]*)"" class=""b-additional-links__link "">(?<name>Товары и услуги|Советы|Энциклопедии|Форумы|Мероприятия)</a>";
            string patternRussia = @"<a href=""(?<uri>[-\w/]*)"" class=""b-additional-links__link "">(?<name>Россия)</a>";

            Regex regcnt = new Regex(patternCountSites);
            Regex regadd = new Regex(patternAdditonal);
            Regex regrus = new Regex(patternRussia);
            Regex reg = new Regex(pattern);
            WebClient wcli = new WebClient();
            wcli.Proxy = null;
            wcli.BaseAddress = "http://yaca.yandex.ru";
            wcli.Encoding = Encoding.UTF8;

            string page = wcli.DownloadString("http://yaca.yandex.ru/yca/geo/Russia/North_Caucasus/Kabardino-Balkaria/cat/Private_Life/");
            MatchCollection matches = reg.Matches(page);
            int count = int.Parse(regcnt.Matches(page)[0].Groups["cnt"].Value);
            string uriRus = regrus.Matches(page)[0].Groups["uri"].Value;
            MatchCollection mc = regadd.Matches(page);

            foreach (Match match in mc)
            {
                string uri = match.Groups["uri"].ToString();
                string name = match.Groups["name"].ToString();
                Console.WriteLine("action : {0}", getAction(name));
            }

            foreach (Match match in matches)
            {
                string uri = match.Groups["uri"].ToString();
                string name = match.Groups["name"].ToString();
            }

        }

        static void TestGetAll()
        {
            VisitorIntentsRepository rep = new VisitorIntentsRepository(new VisitorIntentsDb(
                DatacenterConfig.Couchbase.CommonCouchbase.Select(x => x.Url),
                GlobalConfig.Couchbase.VisitorIntentsDb.Name,
                GlobalConfig.Couchbase.VisitorIntentsDb.Rassword
                ));
            var res = rep.GetAll();
        }

        static void TestYaca()
        {
            string id = HashGenerator.Md5Hash(new Uri("http://www.nr2.ru/").GetShortUrl());
            DownloaderRepository rep = new DownloaderRepository(new DownloaderDb("mongodb://localhost:27017/yaca"));
            var res = rep.GetById(id);
        }
        static void CorrectIds()
        {
            DownloaderRepository rep = new DownloaderRepository(new DownloaderDb("mongodb://localhost:27017/catalogs"));
            DownloaderRepository repsave = new DownloaderRepository(new DownloaderDb("mongodb://localhost:27017/yaca"));
            var bigBase = rep.GetAll();
            int i = 0;
            foreach (var it in bigBase)
            {
                it.Id = null;
                repsave.Save(it);
                if (i++ % 1000 == 0)
                    Console.WriteLine(".");
            }
        }

        static void ParseYaca()
        {
            new Spider();
        }
    }
}
