using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Claymore.NotabilityBot;
using Claymore.SharpMediaWiki;

namespace Claymore.TalkCleanupWikiBot
{
    internal class NotabilityWikification : IModule
    {
        private ArticlesForDeletionLocalization _l10i;
        private NotabilityCriteria _criteria;
        private List<string> _excludedUsers;
        private static Regex _squaredTitleRegex = new Regex(@"\[\[(.+)\]\]");

        private static string[] _notabilityLinks =
            new string[]
                {
                    "Значимость",
                    "КЗ",
                    "Значимость статей",
                    "ЗН",
                    "ОКЗ",
                    "Общий критерий значимости",
                    "ЗНАЧ",
                    "Общие критерии значимости",
                    "НЕНОВОСТИ"
                };

        public NotabilityWikification(ArticlesForDeletionLocalization l10i, NotabilityCriteria criteria, List<string> excldedUsers)
        {
            _l10i = l10i;
            _criteria = criteria;
            _excludedUsers = excldedUsers;
        }

        public void UpdatePages(Wiki wiki, int days)
        {
            Console.Out.WriteLine("Wikify notability...");
            Console.Out.WriteLine("Days: {0}", days);
            Regex wikiLinkRE = new Regex(@"\[{2}(.+?)(\|.+?)?]{2}");
            Regex timeRE = new Regex(@"(\d{2}:\d{2}\, \d\d? [а-я]+ \d{4}) \(UTC\)");

            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("generator", "categorymembers");
            parameters.Add("gcmtitle", _l10i.Category);
            parameters.Add("gcmlimit", "max");
            parameters.Add("gcmnamespace", "4");
            parameters.Add("prop", "info|revisions");
            parameters.Add("intoken", "edit");
            XmlDocument doc = wiki.Enumerate(parameters, true);

            string queryTimestamp = DateTime.Now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

            XmlNodeList pages = doc.SelectNodes("//page");
            foreach (XmlNode page in pages)
            {
                string starttimestamp = queryTimestamp;
     //          int results = 0;
                string prefix = _l10i.MainPage + "/";
                string pageName = page.Attributes["title"].Value;

                string basetimestamp = page.FirstChild.FirstChild.Attributes["timestamp"].Value;
                string editToken = page.Attributes["edittoken"].Value;

                if (pageName.Length < prefix.Length)
                {
                    continue;
                }
                string date = pageName.Substring(prefix.Length);
                Day day = new Day();
                if (!DateTime.TryParse(RuDateTime.MonthNormalize(date), CultureInfo.CreateSpecificCulture(_l10i.Culture),
                        DateTimeStyles.AssumeUniversal, out day.Date))
                {
                    continue;
                }

                if(DateTime.UtcNow.Subtract(day.Date).Days > days)
                    continue;

                string text = "";
                
               try
                    {
                        Console.Out.WriteLine("Downloading " + pageName + "...");
                        text = wiki.LoadTextRev(pageName);
                    }
                    catch (WikiPageNotFound)
                    {
                        continue;
                    }
             

                day.Page = WikiPage.Parse(pageName, text);
                string newtext = text;
                foreach (WikiPageSection section in day.Page.Sections)
                {
                    ReplaceEmptyResults(section);
                    if(IsStriked(section))
                        continue;
                    if (section.Subsections.Count(s => _l10i.Processor != null
                        ? _l10i.Results.Any(r => r == _l10i.Processor(s).Trim())
                        : _l10i.Results.Any(r => r == s.Title.Trim())) == 0 &&
                        section.Subsections.Count(s => s.Title.Trim() == _l10i.ChallengedResult) == 0)
                    {
                        Match m = timeRE.Match(ReplaceSmallText(section.SectionText));
                        string mainTextPart;
                        string outerTextPart;
                        if (m.Success)
                        {
                            mainTextPart = section.SectionText.Substring(0, m.Index);
                            outerTextPart = section.SectionText.Substring(m.Index + m.Length);
                        }
                        else
                        {
                            mainTextPart = section.SectionText;
                            outerTextPart = section.SectionText;
                        }
                        if(ContainsNotabilityLink(mainTextPart))
                            continue;
                        if(WrittenByExcludedUser(mainTextPart))
                            continue;
                        string patchedText = MarkNotabilityWord(mainTextPart, section.Title, wiki);
                        if (patchedText.Length != 0)
                            newtext = newtext.Replace(mainTextPart, patchedText);
                    }
                }
             
                if(text == newtext)
                    continue;
                try
                {
                    Console.Out.WriteLine("Updating " + pageName + "...");
                    string revid = wiki.Save(pageName,
                        "",
                        newtext,
                        "Викификация значимости",
                        MinorFlags.Minor,
                        CreateFlags.NoCreate,
                        WatchFlags.None,
                        SaveFlags.Replace,
                        true,
                        basetimestamp,
                        "",
                        editToken);
                }
                catch (WikiException)
                {
                } 
            }
        }

        private bool IsStriked(WikiPageSection section)
        {
            return section.Title.Contains("<s>");
        }

        private void ReplaceEmptyResults(WikiPageSection section)
        {
            WikiPageSection result = section.Subsections.FirstOrDefault(s => _l10i.Processor != null
                        ? _l10i.Results.Any(r => r == _l10i.Processor(s).Trim())
                        : _l10i.Results.Any(r => r == s.Title.Trim()));
            if (result != null && result.Subsections.Count == 0 &&
                string.IsNullOrEmpty(result.SectionText.Trim()))
            {
                result.Title = _l10i.EmptyResult;
            }
            section.ForEach(ReplaceEmptyResults);
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

        public delegate string TitleProcessor(WikiPageSection section);

        private static List<WikiPageSection> SubsectionsList(WikiPageSection section,
            List<WikiPageSection> aggregator)
        {
            Regex wikiLinkRE = new Regex(@"\[{2}(.+?)(\|.+?)?]{2}");
            Match m = wikiLinkRE.Match(section.Title);
            if (m.Success)
            {
                aggregator.Add(section);
            }
            return section.Reduce(aggregator, SubsectionsList);
        }


        private static string GenerateConcatForXPath(string a_xPathQueryString)
        {
            string returnString = string.Empty;
            string searchString = a_xPathQueryString;
            char[] quoteChars = new char[] { '\'', '"' };

            int quotePos = searchString.IndexOfAny(quoteChars);
            if (quotePos == -1)
            {
                returnString = "'" + searchString + "'";
            }
            else
            {
                returnString = "concat(";
                while (quotePos != -1)
                {
                    string subString = searchString.Substring(0, quotePos);
                    returnString += "'" + subString + "', ";
                    if (searchString.Substring(quotePos, 1) == "'")
                    {
                        returnString += "\"'\", ";
                    }
                    else
                    {
                        //must be a double quote
                        returnString += "'\"', ";
                    }
                    searchString = searchString.Substring(quotePos + 1,
                                     searchString.Length - quotePos - 1);
                    quotePos = searchString.IndexOfAny(quoteChars);
                }
                returnString += "'" + searchString + "')";
            }
            return returnString;
        }

        private static string ReplaceSmallText(string text)
        {
            int pos = 0;
            while (true)
            {
                pos = text.IndexOf("<small>", pos);
                if (pos < 0)
                    break;
                var pos1 = text.IndexOf("</small>", pos);
                if (pos1 < 0)
                    break;
                text = text.Remove(pos, pos1 - pos + 8).Insert(pos, new string('_', pos1 - pos + 8));
            }

            pos = 0;
            while (true)
            {
                pos = text.IndexOf("{{smalldiv|", pos);
                if (pos < 0)
                    break;
                var pos1 = text.IndexOf("}}", pos);
                if (pos1 < 0)
                    break;
                text = text.Remove(pos, pos1 - pos + 8).Insert(pos, new string('_', pos1 - pos + 8));
            }

            return text;
        }

        private bool ContainsNotabilityLink(string text)
        {
            Regex wikiLinkRE = new Regex(@"\[{2}(.+?)(\|.+?)?]{2}");
            MatchCollection matches = wikiLinkRE.Matches(text);
            
            foreach (Match match in matches)
            {
                string pureTitle;
                if (match.Groups[1].Value.ToLower().StartsWith("вп:"))
                    pureTitle = match.Groups[1].Value.Substring(3).Trim();
                else if (match.Groups[1].Value.ToLower().StartsWith("википедия:"))
                    pureTitle = match.Groups[1].Value.Substring(10).Trim();
                else 
                    continue;
              //  if (_notabilityLinks.Contains(pureTitle))
                if (_criteria.IsThisNotabilityLink(pureTitle))
                {
                    return true;
                }
            }

            return false;
        }

        private bool WrittenByExcludedUser(string text)
        {
            Regex wikiLinkRE = new Regex(@"\[{2}(User:|U:|Участник:|Участница:|У:)(.+?)(\|.+?)?]{2}");
            Match m = wikiLinkRE.Match(text);
            if (!m.Success)
                return false;
            string username = m.Groups[2].Value.Trim();

            return _excludedUsers.Any(excludedUser => CapitalizeTitle(excludedUser) == CapitalizeTitle(username));
        }

        private string MarkNotabilityWord(string text, string title, Wiki wiki)
        {
            if(string.IsNullOrEmpty(text))
                return "";
            var pos = text.Length;
            var copytext = text;
            while(true)
            {
                pos = copytext.LastIndexOf("[[", pos);
                if (pos < 0)
                    break;
                var pos1 = copytext.IndexOf("]]", pos);
                if (pos1 > 0)
                    copytext = copytext.Remove(pos, pos1 + 2 - pos).Insert(pos, new string('_', pos1 - pos + 2));
                else
                    copytext = copytext.Remove(pos, 2).Insert(pos, new string('_', 2));
            }
            pos = text.Length;
            while (true)
            {
                pos = copytext.LastIndexOf("{{", pos);
                if (pos < 0)
                    break;
                var pos1 = copytext.IndexOf("}}", pos);
                if (pos1 > 0)
                    copytext = copytext.Remove(pos, pos1 + 2 - pos).Insert(pos, new string('_', pos1 - pos + 2));
                else
                    copytext = copytext.Remove(pos, 2).Insert(pos, new string('_', 2));
            }
            pos = text.Length;
            while (true)
            {
                pos = copytext.LastIndexOf("[http", pos);
                if (pos < 0)
                    break;
                var pos1 = copytext.IndexOf("]", pos);
                if (pos1 > 0)
                    copytext = copytext.Remove(pos, pos1 + 1 - pos).Insert(pos, new string('_', pos1 - pos + 1));
                else
                    copytext = copytext.Remove(pos, 5).Insert(pos, new string('_', 5));
            }


            Regex notabilityRE = new Regex(@"\b([Нн]езначим[а-я]*|[Зз]начим[а-я]*)\b");
            var m = notabilityRE.Match(copytext);
            if (!m.Success)
                return "";

            string notabilityWord = m.Groups[1].Value;
            List<string> categories = GetCategories(wiki, title);
            string notabilityLink = GetRelatedLink(categories);
            string linkedNotability = string.Format("[[Википедия:{0}|{1}]]", notabilityLink, notabilityWord);

            text = text.Remove(m.Groups[1].Index, notabilityWord.Length).Insert(m.Index, linkedNotability);

            return text;
        }

        private List<string> GetCategories(Wiki wiki, string title)
        {
            List<string> categories = new List<string>();
            Match m = _squaredTitleRegex.Match(title);
            if(!m.Success)
            {
                return categories;
            }
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("titles", m.Groups[1].Value.Trim());
            parameters.Add("climit", "max");
            parameters.Add("prop", "categories");
            //parameters.Add("intoken", "edit");
            XmlDocument doc = wiki.Enumerate(parameters, true);

            XmlNodeList xmlCats = doc.SelectNodes("//cl");
            foreach (XmlNode cat in xmlCats)
            {
                string catName = RemovePreffix(cat.Attributes["title"].Value);
                if(catName.Length > 0)
                    categories.Add(catName);
            }

            return categories;
        }

        private string GetRelatedLink(List<string> categories)
        {
            return _criteria.FindNoRelatedCriterion(categories);
        }

        private static string RemovePreffix(string title)
        {
            if (string.IsNullOrEmpty(title))
                return "";
            int pos = title.IndexOf(":");
            if (pos < 0)
                return "";
            return title.Substring(pos + 1).Trim();
        }

        private static string CapitalizeTitle(string src)
        {
            if (string.IsNullOrEmpty(src))
                return src;
            return src[0].ToString().ToUpper() + src.Substring(1);
        }

        #region IModule Members

        public void Run(Wiki wiki, int days)
        {
            UpdatePages(wiki, days);
        }

        #endregion
    }
}
