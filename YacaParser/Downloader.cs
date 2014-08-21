using NLog;
using System;
using System.Collections.Generic;
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
    class Downloader
    {
        Logger log = LogManager.GetCurrentClassLogger();

        private string _link;
        private readonly string _catalogName;
        //private readonly string _parentCatalog;
        private readonly Dictionary<string, YandexCatalog> _cache;
        private readonly Action<YandexCatalog> _action;

        private WebClient wcli;

        private const string patternLink = @"<a href=""([\w\p{P}\p{S}]*)"" class=""b-result__name(?: b-result__name_top)?""";
        private const string patternNext = @"<a class=""b-pager__next"" href=""([-\w/.]*)"">(\w)*</a>";
        private const string patternAllSites = @"<a href=""([\w/]*)"">[Вв]се сайты( рубрики)?</a>";
        private const string patternDescription = @"<p class=""b-result__info"">(?<desc>.*)</p>";
        private const string patternQuote = @"(?<=<span class=""b-result__quote"">Цитируемость:\s)\d+(?=</span>)";
        public Downloader(string link, string currentCatalog, Action<YandexCatalog> action, Dictionary<string, YandexCatalog> cache)//, WebClient cli)
        {
            _link = link;
            _catalogName = currentCatalog;
            //_parentCatalog = parentCatalog;
            _cache = cache ?? Spider.AllSites;
            _action = action;

            //wcli = cli;
            wcli = new WebClient();
            wcli.Proxy = null;
            wcli.BaseAddress = "http://yaca.yandex.ru";
            wcli.Encoding = Encoding.UTF8;
        }

        public static void WaitCallback(object state){
            StateOptions opt = (StateOptions)state;
            new Downloader(opt.link, opt.catalog, opt.action, opt.cache).Processing();
        }

        public void Processing()
        {
            Console.WriteLine("Start : {0}", _catalogName);
            do
            {
                try
                {
                    string root = Spider.DownloadPage(_link);

                    if (root == null)
                    {
                        log.Error("Ошибка. Страница не получена({0}).", _link);
                        Console.WriteLine("Ошибка. Страница не получена({0}).", _link);
                        return;
                    }

                    //Если возможно открыть все сайты рубрики
                    Regex rr = new Regex(patternAllSites);
                    if (rr.IsMatch(root))
                    {
                        _link = rr.Match(root).Groups[1].Value;
                        continue;
                    }

                    Regex reg = new Regex(patternLink);
                    MatchCollection matches = reg.Matches(root);

                    int matchCount = matches.Count;
                    int[] beginsMatches = new int[matchCount+1];
                    Regex regDescr = new Regex(patternDescription);
                    Regex regQuote = new Regex(patternQuote);
                    for (int i = 0; i < matchCount; i++)
                    {
                        beginsMatches[i] = matches[i].Index;
                    }
                    beginsMatches[matchCount] = root.Length;
                    for (int i = 0; i < matchCount; i++)
                    {
                        string uri = matches[i].Groups[1].Value;

                        YandexCatalog s = null;
                        if (_cache.ContainsKey(uri))
                        {
                            s = _cache[uri];
                        }
                        else
                        {
                            string descr = "";
                            Match descrMatch = regDescr.Match(root, beginsMatches[i], beginsMatches[i+1] - beginsMatches[i]);
                            if (descrMatch.Success)
                            {
                                descr = descrMatch.Groups["desc"].Value;
                            }

                            int? quote = null;
                            Match quoteMatch = regQuote.Match(root, beginsMatches[i], beginsMatches[i + 1] - beginsMatches[i]);
                            if (quoteMatch.Success)
                            {
                                try
                                {
                                    quote = int.Parse(quoteMatch.Value);
                                }
                                catch { }
                            }

                            s = new YandexCatalog { Uri = uri, Description = descr, Quote = quote};
                            _cache.Add(uri, s);
                        }
                        _action(s);
                    }
                    Spider.countVisitsOnPages++;
                    Console.Write(".");

                    reg = new Regex(patternNext);
                    Match nextPage = reg.Match(root);

                    if (nextPage.Success)
                        _link = nextPage.Groups[1].Value;
                    else break;

                }
                catch (Exception exc)
                {
                    log.Error("DownloaderError {0} : {1}", _link, exc);
                    Console.WriteLine("#####DownloaderError##{0}", _link);
                }
            } while (true);
            Console.WriteLine("Finish : {0}", _catalogName);
        }
    }
}
