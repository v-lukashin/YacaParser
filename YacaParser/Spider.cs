using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using YacaParser.Models;

namespace YacaParser
{
    class Spider
    {
        public static Dictionary<string, YandexCatalog> AllSites = new Dictionary<string, YandexCatalog>(150000);//здесь хранится весь каталог

        public static long countVisitsOnPages = 0;//количество пройденых страниц
        private HashSet<string> _visitedPages;//uri посещенных страниц

        private const int _poolSize = 100;

        private Stopwatch sw;
        private DateTime _prevTimeSaving;
        private TimeSpan _periodSaving = TimeSpan.FromMinutes(10);

        Logger log = LogManager.GetCurrentClassLogger();

        private static readonly CatModel rootElement = new CatModel { Uri = "/yca/cat/", Name = "ROOT", Parent = "", FullPath = "/ROOT", Geo = "Все регионы" };
        private static readonly CatModel russiaElement = new CatModel { Uri = "/yca/geo/Russia/", Name = "ROOT", Parent = "", FullPath = "/ROOT", Geo = "Россия" };
        Queue<CatModel> queue;

        private readonly DownloaderRepository _rep;
        private const string _connectionString = "mongodb://localhost:27017/YandexCatalog";

        private const string pattern = @"<a href=""(?<uri>[-\w/']*)"" class=""b-rubric__list__item__link"">(?<name>[-\w,. ]*)</a>";
        private const string patternGeoDropdown = @"<a href=""(?<uri>[-\w/]*)"" class=""b-dropdown__link"">(?<name>[-\w ]*)</a>";
        //private const string patternRussia = @"<a href=""(?<uri>[-\w/]*)"" class=""b-additional-links__link "">(?<name>Россия)</a>";
        private const string patternSynt2 = @"<a href=""(?<uri>[-\w/]*)"" class=""b-additional-links__link "">(?<name>Товары и услуги|Советы|Энциклопедии|Форумы|Мероприятия)</a>";
        private const string patternCountSites = @"<h2 class=""b-site-counter__number"">(?<cnt>\d*)[ \w]*</h2>";

        public Spider()
        {
            ThreadPool.SetMaxThreads(_poolSize, _poolSize);
            var date = DateTime.Now;
            var cs = string.Format(_connectionString + "_{0:d2}{1:d2}", date.Month, date.Day);
            _rep = new DownloaderRepository(new DownloaderDb(cs));

            _visitedPages = new HashSet<string>();

            ReadState();
            if (queue == null || queue.Count == 0)
            {
                queue = new Queue<CatModel>(new CatModel[] { russiaElement, rootElement });
            }

            Console.Write("Download from db...");
            var all = _rep.GetAll();
            foreach (var it in all)
            {
                AllSites.Add(it.Uri, it);
            }
            Console.WriteLine(AllSites.Count + " done");

            Console.WriteLine("Start {0}", DateTime.Now);

            Task consoleTask = new Task(ConsoleComand);
            consoleTask.Start();

            sw = Stopwatch.StartNew();
            _prevTimeSaving = DateTime.Now;
            try
            {
                log.Info("Start processing");
                Processing();
                ProcOther();
                log.Info("Finish processing");
            }
            finally
            {
                //сохранить после освобождения пула
                int a = 0, s;
                while (a < _poolSize - 1)
                {
                    ThreadPool.GetAvailableThreads(out a, out s);
                    Thread.Sleep(10000);
                }
                Saving();
                Console.WriteLine("Finish {0}\tTime worked {1}min\nPress any key to exit", DateTime.Now, sw.Elapsed.TotalMinutes);
                Console.ReadKey();
            }
        }

        void Processing()
        {
            Regex reg = new Regex(pattern);
            string link = "";

            while (queue.Any())
            {
                try
                {
                    SaveState();
                    CatModel catModel = queue.Dequeue();
                    log.Info("Извлечен следующий каталог {0}", catModel.Uri);

                    //Пропарсить каталог
                    if (catModel.Name != "ROOT")//не обрабатываем каталоги ROOT, т.к. занимет примерно треть времени(непомеченными останутся около 800)
                    {
                        Action<YandexCatalog> action = y =>
                        {
                            y.Catalog = catModel.Name;
                            y.Parent = catModel.Parent;
                            y.FullPath = catModel.FullPath;
                            if (!y.Geo.Contains(catModel.Geo))
                                y.Geo.Add(catModel.Geo);
                        };

                        WaitCallback(new StateOptions(catModel.Uri, catModel.Name, action));
                    }
                    //сохранение не раньше 10 минут
                    if (_prevTimeSaving < DateTime.Now - _periodSaving)
                    {
                        Saving();
                        _prevTimeSaving = DateTime.Now;
                    }

                    link = catModel.Uri;
                    string page = DownloadPage(link);

                    //Ищем подкаталоги
                    MatchCollection matches = reg.Matches(page);
                    foreach (Match match in matches)
                    {
                        string uri = match.Groups["uri"].Value;
                        string name = match.Groups["name"].Value;

                        //Если не посещали
                        if (!_visitedPages.Contains(uri))
                        {
                            //добавить в очередь
                            queue.Enqueue(new CatModel { Uri = uri, Name = name, Parent = catModel.Name, Geo = catModel.Geo, FullPath = catModel.FullPath + "/" + name });

                            Console.WriteLine("href : {0}// Name : {1}", uri, name);
                            _visitedPages.Add(uri);
                        }
                        else
                        {
                            Console.WriteLine("------Каталог уже был--{0}--{1}-------", uri, name);
                            log.Info("------Каталог уже был--{0}--{1}-------", uri, name);
                        }
                    }
                }
                catch (Exception e)
                {
                    log.Error("SpiderError {0} : {1}", link, e);
                    Console.WriteLine("###SpiderError#{0}", link);
                }
            }
            Saving();
        }
        public static void WaitCallback(object state)
        {
            StateOptions opt = (StateOptions)state;
            string link = opt.link;
            string catalog = opt.catalog;
            Action<YandexCatalog> action = opt.action;

            Console.WriteLine("Обработка ссылки {0}", link);
            //Обработка текущей страницы 
            ThreadPool.QueueUserWorkItem(Downloader.WaitCallback, new StateOptions(link, catalog, action, opt.cache));

            string page = DownloadPage(link);
            //Сначала synt2 обрабатываем, потом переходим к вложеным геолокациям
            for (int i = 0; i < 2; i++)
            {
                Regex reg = i == 0 ? new Regex(patternSynt2) : new Regex(patternGeoDropdown);

                MatchCollection matches = reg.Matches(page);

                foreach (Match match in matches)
                {
                    string uri = match.Groups["uri"].Value;
                    string name = match.Groups["name"].Value;

                    Action<YandexCatalog> act =
                        (
                            i == 0
                            ? GetAction(name)
                            : y => { if (!y.Geo.Contains(name)) y.Geo.Add(name); }
                        )
                        + action;

                    StateOptions options = new StateOptions(uri, catalog, act, opt.cache);
                    if (i == 0)
                    {
                        //Запускаем на обкачку в новом потоке
                        ThreadPool.QueueUserWorkItem(Downloader.WaitCallback, options);
                    }
                    else
                    {
                        //В этом же потоке переходим к следующей геолокации
                        WaitCallback(options);
                        //ThreadPool.QueueUserWorkItem(this.WaitCallback, options);
                    }
                }
            }
        }
        //Для ProcOther не сохраняется статус
        void ProcOther()
        {
            StateOptions opt = new StateOptions("/yca/cat/Blogs", "Blogs", y => y.IsBlog = true);
            ThreadPool.QueueUserWorkItem(Downloader.WaitCallback, opt);

            Regex reg = new Regex(pattern);
            Queue<string> q = new Queue<string>();
            q.Enqueue("/school");

            string link;
            while (q.Any())
            {
                link = q.Dequeue();
                string page = DownloadPage(link);
                Regex regCnt = new Regex(patternCountSites);
                int cnt = int.Parse(regCnt.Match(page).Groups["cnt"].Value);

                opt = new StateOptions(link, "School", y => y.IsSchool = true);
                ThreadPool.QueueUserWorkItem(Downloader.WaitCallback, opt);


                if (cnt > 1000)
                {
                    MatchCollection mc = reg.Matches(page);
                    foreach (Match match in mc)
                    {
                        q.Enqueue(match.Groups["uri"].Value);
                    }
                }
            }
        }

        static Action<YandexCatalog> GetAction(string name)
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

        /// <summary>
        /// 10 попыток получить страницу. Если null, значит получить не удалось
        /// </summary>
        /// <param name="link"></param>
        /// <returns></returns>
        public static string DownloadPage(string link)
        {
            //Stopwatch sw = Stopwatch.StartNew();
            WebClient cli = new WebClient();
            cli.BaseAddress = "http://yaca.yandex.ru";
            cli.Proxy = null;
            cli.Encoding = Encoding.UTF8;

            string page = null;
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    page = cli.DownloadString(link);
                    break;
                }
                catch (WebException wexc)
                {
                    Console.WriteLine("TimeoutError({0}). Repeat", i);
                    continue;
                }
            }
            //sw.Stop();
            //Console.WriteLine("D time = {0}", sw.Elapsed.TotalMilliseconds);
            return page;
        }

        void Saving()
        {
            SaveState();

            Console.Write("Saving...");
            var val = AllSites.Values.ToArray();
            foreach (var v in val)
            {
                _rep.Save(v);
            }
            Console.WriteLine("done");
            log.Info("Saved {0} items. Visits {1}. Time {2}min", val.Length, countVisitsOnPages, sw.Elapsed.TotalMinutes);
        }

        void SaveState()
        {
            Console.Write("Saving state...");
            using (FileStream fs = new FileStream("State.txt", FileMode.Create))
            {
                string jsonStr = JsonConvert.SerializeObject(queue);
                byte[] res = System.Text.Encoding.UTF8.GetBytes(jsonStr);
                fs.Write(res, 0, res.Length);
            }
            Console.WriteLine("done");
        }

        void ReadState()
        {
            Console.Write("Reading state...");
            using (FileStream fs = new FileStream("State.txt", FileMode.OpenOrCreate))
            {
                byte[] byteArr = new byte[fs.Length];
                fs.Read(byteArr, 0, byteArr.Length);
                Queue<CatModel> res = JsonConvert.DeserializeObject<Queue<CatModel>>(System.Text.Encoding.UTF8.GetString(byteArr));
                queue = res;
            }
            Console.WriteLine("done");
        }

        void ConsoleComand()
        {
            while (true)
            {
                string line = Console.ReadLine();
                var shift = "\t\t\t\t\t\t\t";
                Console.Write(shift);
                int a, s;
                switch (line)
                {
                    case "save": Saving(); break;
                    case "all":
                    default: Console.WriteLine("cnt = {0}", AllSites.Count);
                            Console.WriteLine(shift + "Visits on pages {0}", countVisitsOnPages);
                            Console.WriteLine(shift + "Queue lenght {0}", queue.Count);
                            ThreadPool.GetAvailableThreads(out a, out s); Console.WriteLine(shift + "Available threads {0}/{1}", a, _poolSize);
                            Console.WriteLine(shift + "Time working {0:f4}min", sw.Elapsed.TotalMinutes);
                            break;
                }
            }
        }
    }
}
