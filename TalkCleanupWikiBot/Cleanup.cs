using System;
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
    internal class Cleanup : IModule
    {
        internal struct Localization
        {
            public string Language;
            public string Category;
            public string MainPage;
            public CultureInfo Culture;
            public string Template;
            public string TopTemplate;
            public string SectionTitle;
            public string BottomTemplate;
            public TitleProcessor Processor;
            public string MainPageUpdateComment;
            public Regex closedRE;
            public string CloseComment;
            public ClosePage ClosePage;
            public string MainPageSection;
            public string ArchiveTemplate;
            public string ArchivePage;
            public string EmptyArchive;
            public string NavigationTemplate;
            public string ArchiveHeader;
            public string ArchiveFooter;
        }

        public delegate string TitleProcessor(WikiPageSection section);
        public delegate string ClosePage(string text);

        private readonly string _cacheDir;
        private readonly Localization _l10i;

        public Cleanup(Localization l10i)
        {
            _l10i = l10i;
            _cacheDir = string.Format("Cache{0}{1}{0}Cleanup{0}", Path.DirectorySeparatorChar, _l10i.Language);
            
            Directory.CreateDirectory(_cacheDir);
        }

        public void Analyze(Wiki wiki)
        {
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("generator", "categorymembers");
            parameters.Add("gcmtitle", _l10i.Category);
            parameters.Add("gcmlimit", "max");
            parameters.Add("gcmnamespace", "4");
            parameters.Add("prop", "info");

            XmlDocument doc = wiki.Enumerate(parameters, true);
            XmlNodeList pages = doc.SelectNodes("//page");

            List<Day> days = new List<Day>();
            foreach (XmlNode page in pages)
            {
                string prefix = _l10i.MainPage + "/";
                string pageName = page.Attributes["title"].Value;
                string date = pageName.Substring(prefix.Length);
                Day day = new Day();
                try
                {
                    day.Date = DateTime.Parse(RuDateTime.MonthNormalize(date),
                        _l10i.Culture,
                        DateTimeStyles.AssumeUniversal);
                }
                catch (FormatException)
                {
                    continue;
                }

                string fileName = _cacheDir + date + ".bin";

                string text = "";
                if (File.Exists(fileName))
                {
                    using (FileStream fs = new FileStream(fileName, FileMode.Open))
                    using (GZipStream gs = new GZipStream(fs, CompressionMode.Decompress))
                    using (TextReader sr = new StreamReader(gs))
                    {
                        string revid = sr.ReadLine();
                        if (revid == page.Attributes["lastrevid"].Value)
                        {
                            Console.Out.WriteLine("Loading " + pageName + "...");
                            text = sr.ReadToEnd();
                        }
                    }
                }
                if (string.IsNullOrEmpty(text))
                {
                    Console.Out.WriteLine("Downloading " + pageName + "...");
                    text = wiki.LoadTextRev(pageName);
                    using (FileStream fs = new FileStream(fileName, FileMode.Create))
                    using (GZipStream gs = new GZipStream(fs, CompressionMode.Compress))
                    using (StreamWriter sw = new StreamWriter(gs))
                    {
                        sw.WriteLine(page.Attributes["lastrevid"].Value);
                        sw.Write(text);
                    }
                }

                Match m = _l10i.closedRE.Match(text);
                if (m.Success)
                {
                    Console.Out.WriteLine("Closing " + pageName + "...");
                    text = _l10i.ClosePage(text);
                    Save(wiki, pageName, text, _l10i.CloseComment);
                    continue;
                }

                day.Page = WikiPage.Parse(pageName, text);
                days.Add(day);
            }

            days.Sort(CompareDays);

            using (StreamWriter sw =
                        new StreamWriter(_cacheDir + "MainPage.txt"))
            {
                if (!string.IsNullOrEmpty(_l10i.SectionTitle))
                {
                    sw.WriteLine("== " + _l10i.SectionTitle + " ==\n");
                }
                sw.WriteLine("{{#invoke:RequestTable|TableByDate|days=90|header=Статьи, вынесенные на улучшение|link=Википедия:К улучшению\n");

                foreach (Day day in days)
                {
                    sw.Write("|" + day.Date.ToString("yyyy-M-d") + "|\n");

                    List<string> titles = new List<string>();
                    foreach (WikiPageSection section in day.Page.Sections)
                    {
                        RemoveStrikeOut(section);
                        StrikeOutSection(section);
                        int diffSize = 0;
                        if (!section.Title.Contains("<s>"))
                            diffSize = TryGetVersionDiff(wiki, section.Title, day.Date);

                        string result = section.Reduce("", SubsectionsList);
                        if (result.Length > 0)
                        {
                            result = " • <small>" + result.Substring(3) + "</small>";
                        }
                        string title;
                        if (_l10i.Processor != null)
                        {
                            title = _l10i.Processor(section).Trim();
                        }
                        else
                        {
                            title = section.Title.Trim();
                        }
                        if(diffSize > 2000 && !title.Contains("<s>"))
                        {
                            title = "{{Страница дополнена|" + title + "}}";
                        }
                        titles.Add(title + result);
                    }
                    sw.Write(string.Join(" • ", titles.ConvertAll(c => c).ToArray()));
                    sw.Write("\n\n");
                }
                sw.Write("}}\n\n");
                sw.WriteLine("{{" + _l10i.BottomTemplate + "}}");
            }
        }

        public void UpdateMainPage(Wiki wiki)
        {
            Console.Out.WriteLine("Updating articles for cleanup...");
            using (TextReader sr =
                        new StreamReader(_cacheDir + "MainPage.txt"))
            {
                string text = sr.ReadToEnd();
                wiki.SaveSection(_l10i.MainPage,
                    _l10i.MainPageSection,
                    text,
                    _l10i.MainPageUpdateComment);
            }
        }

        public void UpdateArchivePages(Wiki wiki)
        {
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("generator", "categorymembers");
            parameters.Add("gcmtitle", _l10i.Category);
            parameters.Add("gcmlimit", "max");
            parameters.Add("gcmnamespace", "4");
            parameters.Add("prop", "info");

            XmlDocument doc = wiki.Enumerate(parameters, true);
            XmlNodeList pages = doc.SelectNodes("//page");

            DateTime minDate = DateTime.Now;
            foreach (XmlNode page in pages)
            {
                string prefix = _l10i.MainPage + "/";
                string pageName = page.Attributes["title"].Value;
                string date = pageName.Substring(prefix.Length);
                DateTime day;
                if (DateTime.TryParse(RuDateTime.MonthNormalize(date),
                        _l10i.Culture,
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
            DateTime start = minDate;
            while (start <= currentMonth)
            {
                string date = start.ToString("yyyy-MM");
                string pageName = _l10i.ArchivePage + date;
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
                string date = archiveName.Substring(_l10i.ArchivePage.Length);
                DateTime archiveDate;
                if (!DateTime.TryParse(RuDateTime.MonthNormalize(date),
                        _l10i.Culture,
                        DateTimeStyles.AssumeUniversal, out archiveDate))
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
                        _l10i.Culture);
                    string prefix = _l10i.MainPage + "/";
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
                    string prefix = _l10i.MainPage + "/";
                    string pageName = page.Attributes["title"].Value;
                    string dateString = pageName.Substring(prefix.Length);

                    string pageFileName = _cacheDir + dateString + ".bin";
                    Day day = new Day();
                    day.Archived = doc.SelectSingleNode("//page[@title=\"" + pageName + "\"]") == null;

                    if (!DateTime.TryParse(RuDateTime.MonthNormalize(dateString),
                        _l10i.Culture,
                        DateTimeStyles.AssumeUniversal, out day.Date))
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
           //             text = wiki.LoadText(pageName);
                        text = wiki.LoadTextRev(pageName);
                        CachePage(pageFileName, page.Attributes["lastrevid"].Value, text);
                    }

                    day.Exists = true;
                    day.Page = WikiPage.Parse(pageName, text);
                    days.Add(day);
                }

                days.Sort(CompareDays);

                StringBuilder textBuilder = new StringBuilder();
                textBuilder.AppendLine(_l10i.ArchiveHeader);

                StringBuilder sb = new StringBuilder();
                foreach (Day day in days)
                {
                    sb.Append("{{" + _l10i.Template + "|" + day.Date.ToString("yyyy-M-d") + "|\n");
                    if (!day.Exists)
                    {
                        sb.Append("''" + _l10i.EmptyArchive + "''}}\n\n");
                        continue;
                    }

                    List<string> sectionTitles = new List<string>();
                    foreach (WikiPageSection section in day.Page.Sections)
                    {
                        string result = section.Reduce("", SubsectionsList);
                        if (result.Length > 0)
                        {
                            result = " • <small>" + result.Substring(3) + "</small>";
                        }
                        string title;
                        if (_l10i.Processor != null)
                        {
                            title = _l10i.Processor(section).Trim();
                        }
                        else
                        {
                            title = section.Title.Trim();
                        }
                        sectionTitles.Add(title + result);
                    }
                    sb.Append(string.Join(" • ", sectionTitles.ConvertAll(c => c).ToArray()));
                    sb.Append("}}\n\n");
                }
                sb.Replace("<s>", "");
                sb.Replace("</s>", "");
                sb.Replace("<strike>", "");
                sb.Replace("</strike>", "");

                textBuilder.Append(sb.ToString());
                textBuilder.AppendLine(_l10i.ArchiveFooter);

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
                Save(wiki, archiveName, textBuilder.ToString(), _l10i.MainPageUpdateComment);
                using (StreamWriter sw =
                        new StreamWriter(fileName))
                {
                    sw.Write(textBuilder.ToString());
                }
            }
        }

        internal void AddNavigationTemplate(Wiki wiki)
        {
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("list", "embeddedin");
            parameters.Add("eititle", "Template:" + _l10i.NavigationTemplate);
            parameters.Add("eilimit", "max");
            parameters.Add("einamespace", "4");
            //  parameters.Add("eifilterredir", "all");
            Console.WriteLine("Searching template: {0}", "Template:" + _l10i.NavigationTemplate);

            XmlDocument doc = wiki.Enumerate(parameters, true);

            List<string> titles = new List<string>();
            DateTime end = DateTime.Today;
            DateTime start = end.AddDays(-7);
            while (start <= end)
            {
                string pageDate = start.ToString("d MMMM yyyy", _l10i.Culture);
                string prefix = _l10i.MainPage + "/";
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
                        "{{" + _l10i.NavigationTemplate + "}}\n",
                        _l10i.MainPageUpdateComment);
                }
            }
        }

        internal static int CompareDays(Day x, Day y)
        {
            return y.Date.CompareTo(x.Date);
        }

        private void StrikeOutSection(WikiPageSection section)
        {
            Regex wikiLinkRE = new Regex(@"\[{2}(.+?)(\|.+?)?]{2}");

            if (section.Title.ToLower(_l10i.Culture).Contains("{{ok}}") ||
                section.Title.ToLower(_l10i.Culture).Contains("{{ок}}") ||
                section.Title.ToLower(_l10i.Culture).Contains("{{x}}") ||
                section.Subsections.Count(s => s.Title.Trim() == "Итог") > 0)
            {
                if (!section.Title.Contains("<s>"))
                {
                    section.Title = string.Format(" <s>{0}</s> ",
                        section.Title.Trim());
                }

                foreach (WikiPageSection subsection in section.Subsections)
                {
                    Match m = wikiLinkRE.Match(subsection.Title);
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
            if (!section.Title.ToLower(_l10i.Culture).Contains("{{ok}}") &&
                !section.Title.ToLower(_l10i.Culture).Contains("{{ок}}") &&
                !section.Title.ToLower(_l10i.Culture).Contains("{{x}}") &&
                section.Subsections.Count(s => s.Title.Trim() == "Итог") == 0)
            {
                if (section.Title.Contains("<s>"))
                {
                    section.Title = section.Title.Replace("<s>", "");
                    section.Title = section.Title.Replace("</s>", "");
                }
            }
            section.ForEach(RemoveStrikeOut);
        }

        private string SubsectionsList(WikiPageSection section,
           string aggregator)
        {
            Regex wikiLinkRE = new Regex(@"\[{2}(.+?)(\|.+?)?]{2}");
            Match m = wikiLinkRE.Match(section.Title);
            if (m.Success)
            {
                if (_l10i.Processor != null)
                {
                    aggregator = aggregator + " • " + _l10i.Processor(section).Trim();
                }
                else
                {
                    aggregator = aggregator + " • " + section.Title.Trim();
                }
            }
            aggregator = section.Reduce(aggregator, SubsectionsList);
            return aggregator;
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

        private int TryGetVersionDiff(Wiki wiki, string pageName, DateTime day) 
        {
            for (int i = 0; i <= 5; i++) 
            {
                try
                {
                    return GetVersionDiff(wiki, pageName, day);
                }
                catch (Exception e) 
                {
                    Console.WriteLine(e);
                    System.Threading.Thread.Sleep(10000);
                }
            }
            return 0;
        }

        private int GetVersionDiff(Wiki wiki, string pageName, DateTime day) 
        {
            if(!pageName.Contains("[[") || pageName.Contains("<s>"))
                return -1;
            string title = pageName.Replace("[", "").Replace("]", "").Trim();
            if (title.StartsWith(":"))
                title = title.Substring(1).Trim();
            string dateString = string.Format("{0:yyyyMMdd}000000", day.AddDays(1));
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("prop", "revisions");
            parameters.Add("titles", title);
            parameters.Add("rvprop", "timestamp|size|ids");
            parameters.Add("rvend", dateString);
            parameters.Add("rvlimit", "max");

            XmlDocument doc = wiki.Enumerate(parameters, true);

            Dictionary<int, int> revisions = new Dictionary<int, int>();
            XmlNodeList pages = doc.SelectNodes("//rev");            
            foreach (XmlNode page in pages)
            {
                string stringSize = page.Attributes["size"].Value;
                string stringRevId = page.Attributes["revid"].Value;   
                int intSize = Int32.Parse(stringSize);
                int revId = Int32.Parse(stringRevId);
                revisions.Add(revId, intSize);
            }
            if (revisions.Count == 0)
                return 0;
            int startSize = revisions.OrderBy(r => r.Key).First().Value;
            int endSize = revisions.OrderByDescending(r => r.Key).First().Value;


            int diffSize = endSize - startSize;
            return diffSize;
        }

        #region IModule Members

        public void Run(Wiki wiki)
        {
            AddNavigationTemplate(wiki);
            Analyze(wiki);
            UpdateMainPage(wiki);
            UpdateArchivePages(wiki);
        }

        #endregion
    }
}
