﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Claymore.SharpMediaWiki;
using System.Text;
using TalkCleanupWikiBot.Properties;

namespace Claymore.TalkCleanupWikiBot
{
    internal class CategoriesForDiscussion : IModule
    {
        private readonly string _cacheDir;
        private readonly string _language;
        private static readonly Regex _closedRE;
        private static readonly Regex _wikiLinkRE;
        private static readonly Regex _clRE;

        static CategoriesForDiscussion()
        {
            _closedRE = new Regex(@"({{ОБК-Навигация}}\s*{{(Закрыто|Closed|закрыто|closed)}})|({{(Закрыто|Closed|закрыто|closed)}}\s*{{ОБК-Навигация}})");
            _wikiLinkRE = new Regex(@"\[{2}(.+?)(\|.+?)?]{2}");
            _clRE = new Regex(@"\{{2}(cl|ОБК)\|(.+?)\}{2}");
        }

        public CategoriesForDiscussion()
        {
            _language = "ru";
            _cacheDir = string.Format("Cache{0}{1}{0}CategoriesForDiscussion{0}", Path.DirectorySeparatorChar, _language);		
            
            Directory.CreateDirectory(_cacheDir);
        }

        public void Analyze(Wiki wiki)
        {
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("generator", "categorymembers");
            parameters.Add("gcmtitle", "Категория:Википедия:Незакрытые обсуждения категорий");
            parameters.Add("gcmlimit", "max");
            parameters.Add("gcmnamespace", "4");
            parameters.Add("prop", "info");

            XmlDocument doc = wiki.Enumerate(parameters, true);
            XmlNodeList pages = doc.SelectNodes("//page");

            List<Day> days = new List<Day>();
            DateTime start = DateTime.Today;

            foreach (XmlNode page in pages)
            {
                string pageName = page.Attributes["title"].Value;				
                string date = pageName.Substring("Википедия:Обсуждение категорий/".Length);
				Console.WriteLine(pageName);
				Console.WriteLine(date);
                Day day = new Day();
                try
                {
                    day.Date = DateTime.Parse(RuDateTime.MonthNormalize(date),
                        CultureInfo.CreateSpecificCulture("ru-RU"),
                        DateTimeStyles.AssumeUniversal);
                }
                catch (FormatException)
                {
					Console.WriteLine("shit");
                    continue;
                }
				
				Console.WriteLine("{0:yyyy MM dd}", day.Date);

                string fileName = _cacheDir + date + ".bin";
                string text = LoadPageFromCache(fileName,
                    page.Attributes["lastrevid"].Value, pageName);
				
				Console.WriteLine("file: {0}", fileName);

                if (string.IsNullOrEmpty(text))
                {
                    Console.Out.WriteLine("Downloading " + pageName + "...");
                    text = wiki.LoadTextRev(pageName);

                    CachePage(fileName, page.Attributes["lastrevid"].Value, text);
                }
				
				Console.WriteLine("Length: {0}", text.Length);

                Match m = _closedRE.Match(text);
                if (m.Success)
                {
                    Console.Out.WriteLine("Closing " + pageName + "...");
                    text = text.Replace("{{ОБК-Навигация}}", "{{ОБК-Навигация|closed=1}}");
                    wiki.Save(pageName, text, "обсуждение закрыто");
                    continue;
                }

                day.Page = WikiPage.Parse(pageName, text);
                days.Add(day);
				
				Console.WriteLine("OK\n");
            }

            days.Sort(CompareDays);

            using (StreamWriter sw =
                        new StreamWriter(_cacheDir + "MainPage.txt"))
            {
                sw.WriteLine("{{Википедия:Обсуждение категорий/Обсуждаемые категории}}\n");

                foreach (Day day in days)
                {
				//	string dayString = "{{Википедия:Обсуждение категорий/Месяц|" + day.Date.ToString("yyyy-M") + "|\n";
                    sw.Write("{{Википедия:Обсуждение категорий/Месяц|" + day.Date.ToString("yyyy-M") + "|\n");
					var monthDiff = (start.Year - day.Date.Year)*12 + start.Month - day.Date.Month;

                    List<string> titles = new List<string>();
                    foreach (WikiPageSection section in day.Page.Sections)
                    {
                        string filler = "";
                        RemoveStrikeOut(section);
                        StrikeOutSection(section);

						List<string> sectionTitles = new List<string>();
						bool hasNotStriked = false;
                        for (int i = 0; i < section.Level - 1; ++i)
                        {
                            filler += "*";
                        }

						sectionTitles.Add (filler + " " + section.Title.Trim ());

                        List<WikiPageSection> sections = new List<WikiPageSection>();
                        section.Reduce(sections, SubsectionsList);
                        foreach (WikiPageSection subsection in sections)
                        {
                            filler = "";
                            for (int i = 0; i < subsection.Level - 1; ++i)
                            {
                                filler += "*";
                            }
                            if (!subsection.Title.Trim ().StartsWith ("<s>") || monthDiff <= 2) 
							{
								sectionTitles.Add (filler + " " + subsection.Title.Trim ());
								hasNotStriked = true;
							}
                        }
						if (hasNotStriked)
							titles.AddRange (sectionTitles);
                    }
                    if (titles.Count(s => s.Contains("=")) > 0)
                    {
                        titles[0] = "2=<div>" + titles[0].Substring(2);
                    }
                    sw.Write(string.Join("\n", titles.ConvertAll(c => c).ToArray()));
                    if (titles.Count(s => s.Contains("=")) > 0)
                    {
                        sw.Write("</div>");
                    }
                    sw.Write("}}\n\n");
                }

                sw.WriteLine("|}");
            }
        }

        public void UpdateMainPage(Wiki wiki)
        {
            Console.Out.WriteLine("Updating categories for discussion...");
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    using (TextReader sr =
                                new StreamReader(_cacheDir + "MainPage.txt"))
                    {
                        string text = sr.ReadToEnd();
                        wiki.Save("Википедия:Обсуждение категорий/Текущие обсуждения",
                            text,
                            "обновление");
                    }
                    break;
                }
                catch (Exception e) 
                {
                    Console.WriteLine(e);
                    System.Threading.Thread.Sleep(10000);
                    string cookieFile = string.Format("Cache{0}ru{0}cookie.jar", Path.DirectorySeparatorChar);
                    WikiCache.Login(wiki, Settings.Default.Login, Settings.Default.Password, cookieFile);
                }
            }
        }

        public void UpdateArchivePages(Wiki wiki)
        {
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("generator", "categorymembers");
            parameters.Add("gcmtitle", "Категория:Википедия:Незакрытые обсуждения категорий");
            parameters.Add("gcmlimit", "max");
            parameters.Add("gcmnamespace", "4");
            parameters.Add("prop", "info");

            XmlDocument doc = wiki.Enumerate(parameters, true);
            XmlNodeList pages = doc.SelectNodes("//page");

            DateTime minDate = DateTime.Now;
            foreach (XmlNode page in pages)
            {
                string pageName = page.Attributes["title"].Value;
                string date = pageName.Substring("Википедия:Обсуждение категорий/".Length);
                DateTime day;
                if (DateTime.TryParse(RuDateTime.MonthNormalize(date),
                        CultureInfo.CreateSpecificCulture("ru-RU"),
                        DateTimeStyles.AssumeUniversal, out day))
                {
                    if (day < minDate)
                    {
                        minDate = day;
                    }
                }
                else
                {
                    continue;
                }
            }

            List<string> titles = new List<string>();
            minDate = new DateTime(minDate.Year, minDate.Month, 1);
            DateTime currentMonth = new DateTime(DateTime.Today.Year,
                DateTime.Today.Month, 1);
            DateTime start = new DateTime(minDate.Year, 1, 1);
            while (start <= currentMonth)
            {
                string date = start.ToString("yyyy");
                string pageName = "Википедия:Обсуждение категорий/Архив/" + date;
                titles.Add(pageName);
                start = start.AddYears(1);
            }

            parameters.Clear();
            parameters.Add("prop", "info");

            XmlDocument archivesDoc = wiki.Query(QueryBy.Titles, parameters, titles);
            pages = archivesDoc.SelectNodes("//page");
            foreach (XmlNode archivePage in pages)
            {
                string archiveName = archivePage.Attributes["title"].Value;
                string date = archiveName.Substring("Википедия:Обсуждение категорий/Архив/".Length);
                int year;
                if (!int.TryParse(RuDateTime.MonthNormalize(date), out year))
                {
                    continue;
                }
                DateTime archiveDate = new DateTime(year, 1, 1);
                string fileName = _cacheDir + "Archive-" + date + ".txt";

                start = archiveDate;
                DateTime end = start.AddYears(1);
                titles.Clear();
                while (start < end)
                {
                    string pageDate = start.ToString("MMMM yyyy",
                        CultureInfo.CreateSpecificCulture("ru-RU"));
                    string prefix = "Википедия:Обсуждение категорий/";
                    string pageName = prefix + pageDate;
                    titles.Add(pageName);

                    start = start.AddMonths(1);
                }

                parameters.Clear();
                parameters.Add("prop", "info");

                List<Day> days = new List<Day>();
                XmlDocument xml = wiki.Query(QueryBy.Titles, parameters, titles);
                XmlNodeList archives = xml.SelectNodes("//page");
                foreach (XmlNode page in archives)
                {
                    string pageName = page.Attributes["title"].Value;
                    string dateString = pageName.Substring("Википедия:Обсуждение категорий/".Length);

                    string pageFileName = _cacheDir + dateString + ".bin";
                    Day day = new Day();

                    if (!DateTime.TryParse(RuDateTime.MonthNormalize(dateString),
                        CultureInfo.CreateSpecificCulture("ru-RU"),
                        DateTimeStyles.AssumeUniversal, out day.Date))
                    {
                        continue;
                    }

                    if (page.Attributes["missing"] != null)
                    {
                        continue;
                    }

                    string text = LoadPageFromCache(pageFileName,
                        page.Attributes["lastrevid"].Value, pageName);

                    if (string.IsNullOrEmpty(text))
                    {
                        Console.Out.WriteLine("Downloading " + pageName + "...");
                        text = wiki.LoadTextRev(pageName);
                        CachePage(pageFileName, page.Attributes["lastrevid"].Value, text);
                    }
                    day.Page = WikiPage.Parse(pageName, text);
                    days.Add(day);
                }

                days.Sort(CompareDays);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{{Википедия:Обсуждение категорий/Обсуждаемые категории}}\n");

                foreach (Day day in days)
                {
                    sb.Append("{{Википедия:Обсуждение категорий/Месяц|" + day.Date.ToString("yyyy-M") + "|\n");

                    List<string> sectionTitles = new List<string>();
                    foreach (WikiPageSection section in day.Page.Sections)
                    {
                        string filler = "";
                        for (int i = 0; i < section.Level - 1; ++i)
                        {
                            filler += "*";
                        }
                        sectionTitles.Add(filler + " " + section.Title.Trim());

                        List<WikiPageSection> sections = new List<WikiPageSection>();
                        section.Reduce(sections, SubsectionsList);
                        foreach (WikiPageSection subsection in sections)
                        {
                            filler = "";
                            for (int i = 0; i < subsection.Level - 1; ++i)
                            {
                                filler += "*";
                            }
                            sectionTitles.Add(filler + " " + subsection.Title.Trim());
                        }
                    }
                    if (sectionTitles.Count(s => s.Contains("=")) > 0)
                    {
                        sectionTitles[0] = "2=<div>" + sectionTitles[0].Substring(2);
                    }
                    sb.Append(string.Join("\n", sectionTitles.ConvertAll(c => c).ToArray()));
                    if (sectionTitles.Count(s => s.Contains("=")) > 0)
                    {
                        sb.Append("</div>");
                    }
                    sb.Append("}}\n\n");
                }

                sb.Append("|}");
                sb.Replace("<s>", "");
                sb.Replace("</s>", "");
                sb.Replace("<strike>", "");
                sb.Replace("</strike>", "");

                if (File.Exists(fileName))
                {
                    using (TextReader sr = new StreamReader(fileName))
                    {
                        string txt = sr.ReadToEnd();
                        if (txt == sb.ToString())
                        {
                            continue;
                        }
                    }
                }

                Console.Out.WriteLine("Updating " + archiveName + "...");               
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        using (TextReader sr =
                                    new StreamReader(_cacheDir + "MainPage.txt"))
                        {
                            string text = sr.ReadToEnd();
                            wiki.Save(archiveName,
                                sb.ToString(),
                                "обновление");
                        }
                        break;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        System.Threading.Thread.Sleep(10000);
                        string cookieFile = string.Format("Cache{0}ru{0}cookie.jar", Path.DirectorySeparatorChar);
                        WikiCache.Login(wiki, Settings.Default.Login, Settings.Default.Password, cookieFile);
                    }
                }
                using (StreamWriter sw =
                        new StreamWriter(fileName))
                {
                    sw.Write(sb.ToString());
                }
            }
        }

        internal static int CompareDays(Day x, Day y)
        {
            return y.Date.CompareTo(x.Date);
        }

        private void StrikeOutSection(WikiPageSection section)
        {
            if (section.Subsections.Count(s => s.Title.Trim() == "Итог") > 0)
            {
                if (!section.Title.Contains("<s>"))
                {
                    section.Title = string.Format(" <s>{0}</s> ",
                        section.Title.Trim());
                }

                foreach (WikiPageSection subsection in section.Subsections)
                {
                    Match m = _wikiLinkRE.Match(subsection.Title);
                    if (m.Success && !subsection.Title.Contains("<s>"))
                    {
                        subsection.Title = string.Format(" <s>{0}</s> ",
                            subsection.Title.Trim());
                    }
                    m = _clRE.Match(subsection.Title);
                    if (m.Success && !subsection.Title.Contains("<s>"))
                    {
                        subsection.Title = string.Format(" <s>{0}</s> ",
                            subsection.Title.Trim());
                    }
                }
            }
            section.ForEach(StrikeOutSection);
        }

        private void RemoveStrikeOut(WikiPageSection section)
        {
            if (section.Subsections.Count(s => s.Title.Trim() == "Итог") == 0)
            {
                if (section.Title.Contains("<s>"))
                {
                    section.Title = section.Title.Replace("<s>", "");
                    section.Title = section.Title.Replace("</s>", "");
                }
            }
            section.ForEach(RemoveStrikeOut);
        }

        private static List<WikiPageSection> SubsectionsList(WikiPageSection section,
            List<WikiPageSection> aggregator)
        {
            if (section.Level < 4)
            {
                aggregator.Add(section);
            }
            else
            {
                Match m = _wikiLinkRE.Match(section.Title);
                if (m.Success)
                {
                    aggregator.Add(section);
                }
                else
                {
                    m = _clRE.Match(section.Title);
                    if (m.Success)
                    {
                        aggregator.Add(section);
                    }
                }
            }
            return section.Reduce(aggregator, SubsectionsList);
        }

        private static string LoadPageFromCache(string fileName,
            string revisionId,
            string pageName)
        {
            if (File.Exists(fileName))
            {
                using (FileStream fs = new FileStream(fileName, FileMode.Open))
                using (GZipStream gs = new GZipStream(fs, CompressionMode.Decompress))
                using (TextReader sr = new StreamReader(gs))
                {
                    string revid = sr.ReadLine();
                    if (revid == revisionId)
                    {
                        Console.Out.WriteLine("Loading " + pageName + "...");
                        return sr.ReadToEnd();
                    }
                }
            }
            return null;
        }

        private static void CachePage(string fileName, string revisionId, string text)
        {
            using (FileStream fs = new FileStream(fileName, FileMode.Create))
            using (GZipStream gs = new GZipStream(fs, CompressionMode.Compress))
            using (StreamWriter sw = new StreamWriter(gs))
            {
                sw.WriteLine(revisionId);
                sw.Write(text);
            }
        }

        #region IModule Members

        public void Run(Wiki wiki)
        {
            Analyze(wiki);
            UpdateMainPage(wiki);
            UpdateArchivePages(wiki);
        }

        #endregion
    }
}
