﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using TalkCleanupWikiBot.Properties;
using Claymore.SharpMediaWiki;

namespace Claymore.TalkCleanupWikiBot
{
    internal class ProposedMerges : IModule
    {
        private readonly string _cacheDir;
        private readonly string _language;
        private static readonly Regex _closedRE;
        private static readonly Regex _wikiLinkRE;

        static ProposedMerges()
        {
            _closedRE = new Regex(@"({{КОБ-Навигация}}\s*{{(Закрыто|Closed|закрыто|closed)}})|({{(Закрыто|Closed|закрыто|closed)}}\s*{{КОБ-Навигация}})");
            _wikiLinkRE = new Regex(@"\[{2}(.+?)(\|.+?)?]{2}");
        }

        public ProposedMerges()
        {
            _language = "ru";
            _cacheDir =  string.Format("Cache{0}{1}{0}ProposedMerges{0}", Path.DirectorySeparatorChar, _language);
            
            Directory.CreateDirectory(_cacheDir);
        }

        public void Analyze(Wiki wiki)
        {
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("generator", "categorymembers");
            parameters.Add("gcmtitle", "Категория:Википедия:Незакрытые обсуждения объединения страниц");
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
                string date = pageName.Substring("Википедия:К объединению/".Length);
                Day day = new Day();
                try
                {
                    day.Date = DateTime.Parse(RuDateTime.MonthNormalize(date),
                        CultureInfo.CreateSpecificCulture("ru-RU"),
                        DateTimeStyles.AssumeUniversal);
                }
                catch (FormatException)
                {
                    continue;
                }

                string fileName = _cacheDir + date + ".bin";
                string text = LoadPageFromCache(fileName,
                    page.Attributes["lastrevid"].Value, pageName);

                if (string.IsNullOrEmpty(text))
                {
                    Console.Out.WriteLine("Downloading " + pageName + "...");
                    text = wiki.LoadTextRev(pageName);

                    CachePage(fileName, page.Attributes["lastrevid"].Value, text);
                }

                Match m = _closedRE.Match(text);
                if (m.Success)
                {
                    Console.Out.WriteLine("Closing " + pageName + "...");
                    text = text.Replace("{{КОБ-Навигация}}", "{{КОБ-Навигация|nocat=1}}");
             //       wiki.Save(pageName, text, "обсуждение закрыто");
                    Save(wiki, pageName, text, "обсуждение закрыто");
                    continue;
                }
                
                day.Page = WikiPage.Parse(pageName, text);
                days.Add(day);
            }

            days.Sort(CompareDays);

            using (StreamWriter sw =
                        new StreamWriter(_cacheDir + "MainPage.txt"))
            {
                sw.WriteLine("== Текущие обсуждения ==\n");
    //            sw.WriteLine("{{Объединение статей/Статьи, вынесенные для объединения}}\n");
                sw.WriteLine("{{#invoke:RequestTable|TableByDate|header=Статьи, вынесенные на обсуждение|link=Википедия:К объединению\n");

                foreach (Day day in days)
                {
           //         sw.Write("{{Объединение статей/День|" + day.Date.ToString("yyyy-M-d") + "|\n");
                    sw.Write("|" + day.Date.ToString("yyyy-M-d") + "|\n");

                    List<string> titles = new List<string>();
                    foreach (WikiPageSection section in day.Page.Sections)
                    {
                        string filler = "";
                        RemoveStrikeOut(section);
                        StrikeOutSection(section);

                        for (int i = 0; i < section.Level - 1; ++i)
                        {
                            filler += "*";
                        }
                        titles.Add(filler + " " + section.Title.Trim().Replace("|", "→"));

                        List<WikiPageSection> sections = new List<WikiPageSection>();
                        section.Reduce(sections, SubsectionsList);
                        foreach (WikiPageSection subsection in sections)
                        {
                            filler = "";
                            for (int i = 0; i < subsection.Level - 1; ++i)
                            {
                                filler += "*";
                            }
                            titles.Add(filler + " " + subsection.Title.Trim());
                        }
                    }
                    if (titles.Count(s => s.Contains("=")) > 0)
                    {
                        titles[0] = "2=<li>" + titles[0].Substring(2) + "</li>";
                    }
                    sw.Write(string.Join("\n", titles.ConvertAll(c => c).ToArray()));
               //     sw.Write("}}\n\n");
                    sw.Write("\n\n");
                }

                sw.WriteLine("}}\n\n");
                sw.WriteLine("{{/Подвал}}");
            }
        }

        public void UpdateArchivePages(Wiki wiki)
        {
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("generator", "categorymembers");
            parameters.Add("gcmtitle", "Категория:Википедия:Незакрытые обсуждения объединения страниц");
            parameters.Add("gcmlimit", "max");
            parameters.Add("gcmnamespace", "4");
            parameters.Add("prop", "info");

            XmlDocument doc = wiki.Enumerate(parameters, true);
            XmlNodeList pages = doc.SelectNodes("//page");

            DateTime minDate = DateTime.Now;
            foreach (XmlNode page in pages)
            {
                string pageName = page.Attributes["title"].Value;
                string date = pageName.Substring("Википедия:К объединению/".Length);
                try
                {
                    DateTime day = DateTime.Parse(RuDateTime.MonthNormalize(date),
                        CultureInfo.CreateSpecificCulture("ru-RU"),
                        DateTimeStyles.AssumeUniversal);
                    if (day < minDate)
                    {
                        minDate = day;
                    }
                }
                catch (FormatException)
                {
                    continue;
                }
            }

            List<string> titles = new List<string>();
            minDate = new DateTime(minDate.Year, minDate.Month, 1);
            DateTime currentMonth = new DateTime(DateTime.Today.Year,
                DateTime.Today.Month, 1);
            DateTime start = minDate;
            while (start <= currentMonth)
            {
                string date = start.ToString("yyyy-MM");
                string pageName = "Википедия:Архив запросов на объединение/" + date;
                titles.Add(pageName);
                start = start.AddMonths(1);
            }

            parameters.Clear();
            parameters.Add("prop", "info");

            XmlDocument archivesDoc = wiki.Query(QueryBy.Titles, parameters, titles);
            pages = archivesDoc.SelectNodes("//page");
            foreach (XmlNode archivePage in pages)
            {
                string archiveName = archivePage.Attributes["title"].Value;
                string date = archiveName.Substring("Википедия:Архив запросов на объединение/".Length);
                DateTime archiveDate;
                try
                {
                    archiveDate = DateTime.Parse(RuDateTime.MonthNormalize(date),
                        CultureInfo.CreateSpecificCulture("ru-RU"),
                        DateTimeStyles.AssumeUniversal);
                }
                catch (FormatException)
                {
                    continue;
                }
                string fileName = _cacheDir + "Archive-" + date + ".txt";
                start = archiveDate;
                DateTime end = start.AddMonths(1);
                titles.Clear();
                while (start < end)
                {
                    string pageDate = start.ToString("d MMMM yyyy",
                        CultureInfo.CreateSpecificCulture("ru-RU"));
                    string prefix = "Википедия:К объединению/";
                    string pageName = prefix + pageDate;
                    titles.Add(pageName);

                    start = start.AddDays(1);
                }

                parameters.Clear();
                parameters.Add("prop", "info");

                List<Day> days = new List<Day>();
                XmlDocument xml = wiki.Query(QueryBy.Titles, parameters, titles);
                XmlNodeList archives = xml.SelectNodes("//page");
                foreach (XmlNode page in archives)
                {
                    string pageName = page.Attributes["title"].Value;
                    string dateString = pageName.Substring("Википедия:К объединению/".Length);

                    string pageFileName = _cacheDir + dateString + ".bin";
                    Day day = new Day();
                    day.Archived = doc.SelectSingleNode("//page[@title=\"" + pageName + "\"]") == null;

                    try
                    {
                        day.Date = DateTime.Parse(RuDateTime.MonthNormalize(dateString),
                            CultureInfo.CreateSpecificCulture("ru-RU"),
                            DateTimeStyles.AssumeUniversal);
                    }
                    catch (FormatException)
                    {
                        continue;
                    }

                    if (page.Attributes["missing"] != null)
                    {
                        day.Exists = false;
                        days.Add(day);
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

                    day.Exists = true;
                    day.Page = WikiPage.Parse(pageName, text);
                    days.Add(day);
                }

                days.Sort(CompareDays);

                StringBuilder textBuilder = new StringBuilder();

                textBuilder.AppendLine("{{Навигация по архиву КОБ}}\n{{Объединение статей/Начало}}");

                StringBuilder sb = new StringBuilder();
                foreach (Day day in days)
                {
                    sb.Append("{{Объединение статей/День|" + day.Date.ToString("yyyy-M-d") + "|\n");
                    if (!day.Archived && day.Exists)
                    {
                        sb.Append("''обсуждение не завершено''}}\n\n");
                        continue;
                    }
                    if (!day.Exists)
                    {
                        sb.Append("''нет обсуждений''}}\n\n");
                        continue;
                    }
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
                        titles[0] = "2=<li>" + titles[0].Substring(2) + "</li>";
                    }
                    sb.Append(string.Join("\n", sectionTitles.ConvertAll(c => c).ToArray()));
                    sb.Append("}}\n\n");
                }
                sb.Replace("<s>", "");
                sb.Replace("</s>", "");
                sb.Replace("<strike>", "");
                sb.Replace("</strike>", "");

                textBuilder.Append(sb.ToString());

                textBuilder.AppendLine("{{Объединение статей/Конец}}");

                if (File.Exists(fileName))
                {
                    using (TextReader sr = new StreamReader(fileName))
                    {
                        string text = sr.ReadToEnd();
                        if (text == textBuilder.ToString())
                        {
                            continue;
                        }
                    }
                }

                Console.Out.WriteLine("Updating " + archiveName + "...");
            //    wiki.Save(archiveName,
            //        textBuilder.ToString(),
            //        "обновление");
                Save(wiki, archiveName, textBuilder.ToString(),"Обновление");
                using (StreamWriter sw =
                        new StreamWriter(fileName))
                {
                    sw.Write(textBuilder.ToString());
                }
            }
        }

        public void UpdateMainPage(Wiki wiki)
        {
            Console.Out.WriteLine("Updating proposed merges...");
            using (TextReader sr =
                        new StreamReader(_cacheDir + "MainPage.txt"))
            {
                string text = sr.ReadToEnd();
                wiki.SaveSection("Википедия:К объединению",
                    "1",
                    text,
                    "обновление");
            }
        }

        public void UpdatePages(Wiki wiki)
        {
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("generator", "categorymembers");
            parameters.Add("gcmtitle", "Категория:Википедия:Незакрытые обсуждения объединения страниц");
            parameters.Add("gcmlimit", "max");
            parameters.Add("gcmnamespace", "4");
            parameters.Add("prop", "info");
            XmlDocument doc = wiki.Enumerate(parameters, true);
            XmlNodeList pages = doc.SelectNodes("//page");

            foreach (XmlNode page in pages)
            {
                string pageName = page.Attributes["title"].Value;
                string date = pageName.Substring("Википедия:К объединению/".Length);
                Day day = new Day();
                try
                {
                    day.Date = DateTime.Parse(RuDateTime.MonthNormalize(date),
                        CultureInfo.CreateSpecificCulture("ru-RU"),
                        DateTimeStyles.AssumeUniversal);
                }
                catch (FormatException)
                {
                    continue;
                }
                string fileName = _cacheDir + date + ".bin";
                string text = LoadPageFromCache(fileName,
                    page.Attributes["lastrevid"].Value, pageName);
                
                if (string.IsNullOrEmpty(text))
                {
                    Console.Out.WriteLine("Downloading " + pageName + "...");
          //          text = wiki.LoadText(pageName);
                    text = wiki.LoadTextRev(pageName);
                    CachePage(fileName, page.Attributes["lastrevid"].Value, text);
                }
                day.Page = WikiPage.Parse(pageName, text);
                foreach (WikiPageSection section in day.Page.Sections)
                {
                    RemoveStrikeOut(section);
                    StrikeOutSection(section);
                }

                string newText = day.Page.Text;
                if (newText.Trim() == text.Trim())
                {
                    continue;
                }
                try
                {
                    string revid = wiki.Save(pageName,
                        newText,
                        "зачёркивание заголовков");

                    CachePage(fileName, revid, newText);
                }
                catch (WikiException)
                {
                }
            }
        }

        internal void AddNavigationTemplate(Wiki wiki)
        {
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("list", "embeddedin");
            parameters.Add("eititle", "Template:КОБ-Навигация");
            parameters.Add("eilimit", "max");
            parameters.Add("einamespace", "4");
            parameters.Add("eifilterredir", "all");

            XmlDocument doc = wiki.Enumerate(parameters, true);

            List<string> titles = new List<string>();
            DateTime end = DateTime.Today;
            DateTime start = end.AddDays(-7);
            while (start <= end)
            {
                string pageDate = start.ToString("d MMMM yyyy",
                        CultureInfo.CreateSpecificCulture("ru-RU"));
                string prefix = "Википедия:К объединению/";
                string pageName = prefix + pageDate;
                if (doc.SelectSingleNode("//ei[@title='" + pageName + "']") == null)
                {
                    titles.Add(pageName);
                }
                start = start.AddDays(1);
            }

            parameters.Clear();
            parameters.Add("prop", "info");
            XmlDocument xml = wiki.Query(QueryBy.Titles, parameters, titles);
            foreach (XmlNode node in xml.SelectNodes("//page"))
            {
                if (node.Attributes["missing"] == null)
                {
                    Console.Out.WriteLine("Updating " + node.Attributes["title"].Value + "...");
                    wiki.Prepend(node.Attributes["title"].Value,
                        "{{КОБ-Навигация}}\n",
                        "добавление навигационного шаблона");
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
            Match m = _wikiLinkRE.Match(section.Title);
            if (m.Success)
            {
                aggregator.Add(section);
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

        private void Save(Wiki wiki, string title, string newtext, string comment)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    wiki.Save(title,
                        newtext,
                        comment);
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

        #region IModule Members

        public void Run(Wiki wiki)
        {
            AddNavigationTemplate(wiki);
            UpdatePages(wiki);
            Analyze(wiki);
            UpdateMainPage(wiki);
            UpdateArchivePages(wiki);
        }

        #endregion
    }
}
