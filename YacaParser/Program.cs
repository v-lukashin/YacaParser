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
            //MarkProduction();
            //MarkRoot();
            //SaveCatalogsPath();
            //TransformYacaToFullPath();
            //Copy("YandexCatalog", "YandexCatalogAddShortUrl");
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
            Console.ReadKey();
        }
        class Catalog
        {
            public Catalog(string n, string u)
            {
                name = n;
                uri = u;
            }
            public string name;
            public string uri;
        }

        static void MarkProduction()
        {
            var connectionString = "mongodb://localhost:27017/yandexCatalog_001";
            var rep = new DownloaderRepository(new DownloaderDb(connectionString));
            foreach (var item in rep.GetAll().Where(x => x.FullPath == null && x.Parent == "Производство и поставки"))
            {
                Console.Write('.');
                item.FullPath = "/ROOT/Производство/"+item.Catalog;
                rep.Save(item);
            }//
            foreach (var item in rep.GetAll().Where(x => x.Parent == "Выпускной" && x.FullPath == null))
            {
                item.Parent = "Все для праздника";
                if (item.Catalog == "Поздравления")
                {
                    item.FullPath = "/ROOT/Дом/Все для праздника/Тосты и сценарии";
                    item.Catalog = "Тосты и сценарии";
                }
                else
                {
                    item.FullPath = "/ROOT/Дом/Все для праздника/Организация праздников";
                    item.Catalog = "Организация праздников";
                }
                rep.Save(item);
            }
        }
        static void MarkRoot()
        {
            var connectionString = "mongodb://localhost:27017/yandexCatalog_001";
            var rep = new DownloaderRepository(new DownloaderDb(connectionString));
            var all = rep.GetAll();
            foreach (var item in rep.GetAll().Where(x=>x.Catalog == "ROOT" && x.Parent == string.Empty))
            {
                Console.Write('.');
                item.FullPath = "/ROOT";
                rep.Save(item);
            }
        }

        static void TransformYacaToFullPath()
        {
            var allSites = new Dictionary<string, YandexCatalog>(150000);
            var connectionString = "mongodb://localhost:27017/yandexCatalog";
            var reg = new Regex(@"(/(?<par>[-\w,. ]+))?/(?<cat>[-\w,. ]+)$");
            var rep = new DownloaderRepository(new DownloaderDb(connectionString));
            //var all = rep.GetAll();
            var listConflict = new List<Catalog>();
            var allCatalogNames = new List<string>();
            var set = new HashSet<string>();
            StreamReader reader = new StreamReader("TmpCatalogs.txt", Encoding.UTF8);
            //Проверка конфликтов
            while (!reader.EndOfStream)
            {
                var catalog = JsonConvert.DeserializeObject<Catalog>(reader.ReadLine().Replace("\n", string.Empty));
                allCatalogNames.Add(catalog.name);
                var match = reg.Match(catalog.name);
                var par = match.Groups["par"].Value;
                var cat = match.Groups["cat"].Value;
                var pair = par + "/" + cat;
                if (set.Contains(pair))
                {
                    listConflict.Add(catalog);
                }
                else set.Add(pair);
                Console.WriteLine(par + "\n" + cat);
                Console.WriteLine("\n");
            }
            Console.WriteLine("=============conflict================" + listConflict.Count);

            Console.Write("Fill allSites...");
            foreach (var yaca in rep.GetAll())
            {
                allSites.Add(yaca.Uri, yaca);
            }
            Console.WriteLine("done");

            Parallel.ForEach(listConflict, cat =>
            {
                var m = reg.Match(cat.name);
                var catName = m.Groups["cat"].Value;
                var parName = m.Groups["par"].Value;
                Spider.WaitCallback(new StateOptions(cat.uri, cat.name, x => {
                    x.FullPath = cat.name;
                    x.Catalog = catName;
                    x.Parent = parName;
                }, allSites));
            });
            Console.WriteLine("Обработка..");

            Parallel.ForEach(allSites.Where(x => string.IsNullOrEmpty(x.Value.FullPath)).ToArray(), s =>
            {
                Console.Write('.');
                foreach (var c in allCatalogNames)
                {
                    var match = reg.Match(c);
                    var par = match.Groups["par"].Value;
                    var cat = match.Groups["cat"].Value;
                    if (cat == s.Value.Catalog && par == s.Value.Parent)
                    {
                        s.Value.FullPath = c;
                        break;
                    }
                }
            });
            Console.Write("Saving...");
            var rep2 = new DownloaderRepository(new DownloaderDb(connectionString + "_001"));
            foreach (var item in allSites)
            {
                rep2.Save(item.Value);
            }
            Console.WriteLine("done");
        }
        static void SaveCatalogsPath()
        {
            string pattern = @"<a href=""(?<uri>[-\w/']*)"" class=""b-rubric__list__item__link"">(?<name>[-\w,. ]*)</a>";

            var set = new HashSet<string> { @"http://yaca.yandex.ru" };
            var queue = new Queue<Catalog>();
            queue.Enqueue(new Catalog("/ROOT", @"http://yaca.yandex.ru"));

            var writer = new StreamWriter("TmpCatalogs.txt", false, Encoding.UTF8);
            while (queue.Any())
            {
                var catalog = queue.Dequeue();
                Console.WriteLine("Next page : {0}", catalog.name);
                var page = Download(catalog.uri);
                var reg = new Regex(pattern);
                var matches = reg.Matches(page);
                foreach (Match match in matches)
                {
                    var uri = match.Groups["uri"].Value;
                    var name = match.Groups["name"].Value;
                    name = catalog.name + "/" + name;

                    var tmp = new Catalog(name, uri);
                    if (!set.Contains(uri))
                    {
                        queue.Enqueue(tmp);
                        set.Add(uri);
                    }

                    writer.WriteLine(JsonConvert.SerializeObject(tmp));
                }
            }
            Console.WriteLine("done");
        }
        static void Copy(string dbNameFrom, string dbNameTo)//Нужно было чтобы дописать ShortUrl в уже созданные DB
        {
            string connStr = "mongodb://localhost:27017/";
            DownloaderRepository repFrom = new DownloaderRepository(new DownloaderDb(connStr + dbNameFrom));
            DownloaderRepository repTo = new DownloaderRepository(new DownloaderDb(connStr + dbNameTo));
            var all = repFrom.GetAll();
            foreach (var it in all)
            {
                repTo.Save(it);
            }
        }

        static void TestShortUrl()
        {
            string connectionString = "mongodb://localhost:27017/YandexCatalog";
            DownloaderRepository rep = new DownloaderRepository(new DownloaderDb(connectionString));
            var all = rep.GetAll();
            int iter = 0;
            foreach (var it in all)
            {
                Console.WriteLine(it.Uri + " -> " + it.ShortUrl);
                iter++;
                if (iter == 20)
                {
                    Console.WriteLine("Press any key to continue..");
                    Console.ReadKey();
                    iter = 0;
                    Console.WriteLine("----------------------------------------");
                }
            }
        }

        static void MergeGeneralWithBlogAndSchool()
        {
            DownloaderRepository rep = new DownloaderRepository(new DownloaderDb("mongodb://localhost:27017/YandexCatalogOtherTest"));
            DownloaderRepository repRes = new DownloaderRepository(new DownloaderDb("mongodb://localhost:27017/YandexCatalogCompiled"));

            IEnumerable<YandexCatalog> sb = rep.GetAll();

            foreach (var y in sb)
            {
                YandexCatalog cat = repRes.GetById(y.Id) ?? new YandexCatalog { Id = y.Id, Uri = y.Uri, Description = y.Description };
                cat.IsBlog = y.IsBlog;
                cat.IsSchool = y.IsSchool;
                repRes.Save(cat);
            }
        }

        static void TestReadSave()
        {
            Queue<CatModel> queue = new Queue<CatModel>();
            queue.Enqueue(new CatModel { Geo = "Rus", Name = "Envir", Parent = "ROOT", Uri = "http://bla.bla" });
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
            string patternQuote = @"(?<=<span class=""b-result__quote"">Цитируемость:\s)\d+(?=</span>)";

            Regex regQuote = new Regex(patternQuote);
            Regex regcnt = new Regex(patternCountSites);
            Regex regadd = new Regex(patternAdditonal);
            Regex regrus = new Regex(patternRussia);
            Regex reg = new Regex(pattern);
            WebClient wcli = new WebClient();
            wcli.Proxy = null;
            wcli.BaseAddress = "http://yaca.yandex.ru";
            wcli.Encoding = Encoding.UTF8;

            string page = wcli.DownloadString("http://yaca.yandex.ru/yca/geo/Russia/North_Caucasus/Kabardino-Balkaria/cat/Private_Life/");
            var matchQuote = regQuote.Matches(page);

            foreach (Match m in matchQuote)
            {
                Console.WriteLine(m.Value);
            }

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
