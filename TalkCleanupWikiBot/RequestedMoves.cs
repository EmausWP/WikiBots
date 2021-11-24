using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Web;
using TalkCleanupWikiBot.Properties;
using Claymore.SharpMediaWiki;

namespace Claymore.TalkCleanupWikiBot
{
    internal class DisputedVerdict
    {
        public string Title;
        public string NominationTitle;
        public DateTime NominationDate;
        public DateTime VerdictDate;
        public DateTime DisputedDate;
        public string NominationPageType;
		public string VerdictAuthor;
		public string FullAuthorString;
    }

    internal class RequestedMoves : IModule
	{
		private string _cacheDir;
		private string _language;
        private List<DisputedVerdict> _oldDisputedVerdicts;
        private List<DisputedVerdict> _newDisputedVerdicts;
        private List<DisputedVerdict> _priorVerdicts;
        private List<DisputedVerdict> _newVerdicts;

		public RequestedMoves ()
		{
			_language = "ru";
			_cacheDir = string.Format("Cache{0}{1}{0}RequestedMoves{0}", Path.DirectorySeparatorChar, _language);
			Directory.CreateDirectory (_cacheDir);
		}

		public void Analyze (Wiki wiki)
		{
			ParameterCollection parameters = new ParameterCollection ();
			parameters.Add ("generator", "categorymembers");
			parameters.Add ("gcmtitle", "Категория:Википедия:Незакрытые обсуждения переименования страниц");
			parameters.Add ("gcmlimit", "max");
			parameters.Add ("gcmnamespace", "4");
			parameters.Add ("prop", "info|revisions");
			parameters.Add ("rvprop", "timestamp");
			XmlDocument doc = wiki.Enumerate (parameters, true);
			XmlNodeList pages = doc.SelectNodes ("//page");
            parameters.Set("gcmtitle", "Категория:Википедия:Закрытые обсуждения переименования страниц");
            XmlDocument closedDoc = wiki.Enumerate(parameters, true);
            XmlNodeList closedPages = closedDoc.SelectNodes("//page");
            
			
			List<Day> openDays = new List<Day> ();
            List<Day> allDays = new List<Day>();
			DateTime start = DateTime.Today;
		
			wiki.SleepBetweenEdits = 3;
			wiki.SleepBetweenQueries = 1;

            UrgencyCheck(wiki, pages, openDays);
            UrgencyCheck(wiki, closedPages, allDays);
			
			DateTime cutOffDate = new DateTime (2009, 3, 21);
		
			openDays.Sort (CompareDays);
		    allDays.AddRange(openDays);
            allDays.Sort(CompareDays);

            GetOldDisputedResults(wiki);
            _newDisputedVerdicts = new List<DisputedVerdict>();
            _priorVerdicts = new List<DisputedVerdict>();
            _newVerdicts = new List<DisputedVerdict>();
            Regex timeRE = new Regex(@"(\d{2}:\d{2}\, \d\d? [а-я]+ \d{4}) \(UTC\)");
            Regex encSymbRe = new Regex(@"%([0-9a-f][0-9a-f])");
            foreach (Day day in allDays)
            {
                Console.Out.WriteLine("Analyzing " + day.Date.ToString("d MMMM yyyy") + "...");
                        
                foreach (WikiPageSection section in day.Page.Sections)
                {
                    bool hasVerdict = section.Subsections.Count(s => s.Title.ToLower().Trim() == "итог") > 0;
                    bool hasAutoVerdict = section.Subsections.Count(s => s.Title.Trim() == "Автоматический итог" || s.Title.Trim() == "Автоитог") > 0;
                    bool hasDisputedVerdict =
                        section.Subsections.Count(s => s.Title.ToLower().Trim() == "оспоренный итог" ||
                                                       s.Title.ToLower().Trim() == "оспореный итог") > 0;
                    bool hasPriorVerdict = section.Subsections.Count(s => s.Title.ToLower().Trim() == "предварительный итог") > 0;

                    if (hasDisputedVerdict)
                        Console.WriteLine(section.Title);

                    if (hasDisputedVerdict && !hasVerdict && !hasAutoVerdict)
                    {
						string verdictText = "";
                        DateTime verdictDate;
                        var dispSection =
                            section.Subsections.First(s => s.Title.ToLower().Trim() == "оспоренный итог" ||
                                                           s.Title.ToLower().Trim() == "оспореный итог");
                        var timeMatch = timeRE.Match(dispSection.Text);
                        if (timeMatch.Success)
                        {
                            DateTime.TryParse(timeMatch.Groups[1].Value.Trim(), 
								CultureInfo.CreateSpecificCulture("ru-RU"),
								DateTimeStyles.AssumeUniversal,
								out verdictDate);
							verdictText = dispSection.Text.Remove (timeMatch.Index);
                        }
                        else
                        {
                            verdictDate = GetLastUpdateDate(wiki, day.Page.Title);
                        }
                        DateTime disputedDate = GetLastUpdateDate(wiki, day.Page.Title);
						var verdictAuthor = GetVerdictAuthor (verdictText);

                        string dayStatus = "Закрыта";
                        if (openDays.Contains(day)) dayStatus = "Открыта";
                        int ind = day.Page.Title.IndexOf("/");
                        string dayName = day.Page.Title.Substring(ind + 1).Trim();
                        string sublink =
                            HttpUtility.UrlEncode(section.Title.Trim().Replace(' ', '_').Replace("[[", "").Replace(
                                "]]", ""));
                        
                        MatchCollection mc = encSymbRe.Matches(sublink);
                        sublink = mc.Cast<Match>().Aggregate(sublink,
                                                             (current, m) =>
                                                             current.Replace(m.Value, "." + m.Groups[1].Value.ToUpper()));

                        DisputedVerdict newDisputedVerdict = new DisputedVerdict();
                        newDisputedVerdict.Title = section.Title.Trim();
                        newDisputedVerdict.NominationTitle =
							string.Format("[[{0}#{1}|{2}]]", 
								day.Page.Title, 
								sublink.Replace("<s>", "").Replace("</s>", "").Trim (new[]{ ' ', '[', ']', ':' }), 
								dayName);
                        newDisputedVerdict.VerdictDate = verdictDate;
                        newDisputedVerdict.DisputedDate = disputedDate;
                        newDisputedVerdict.NominationPageType = dayStatus;
						newDisputedVerdict.VerdictAuthor = verdictAuthor;
                        _newDisputedVerdicts.Add(newDisputedVerdict);
                    }
                    
                    if (hasPriorVerdict && !hasDisputedVerdict && !hasVerdict && !hasAutoVerdict && openDays.Contains(day))
                    {
						string verdictText = "";
                        DateTime verdictDate;
                        var dispSection =
                            section.Subsections.First(s => s.Title.ToLower().Trim() == "предварительный итог");
                        var timeMatch = timeRE.Match(dispSection.Text);
                        if (timeMatch.Success)
                        {
                            DateTime.TryParse(timeMatch.Groups[1].Value.Trim(), 
								CultureInfo.CreateSpecificCulture("ru-RU"),
								DateTimeStyles.AssumeUniversal,
								out verdictDate);
							verdictText = dispSection.Text.Remove (timeMatch.Index);
                        }
                        else
                        {
                            verdictDate = GetLastUpdateDate(wiki, day.Page.Title);
                        }
						var verdictAuthor = GetVerdictAuthor (verdictText);

                        int ind = day.Page.Title.IndexOf("/");
                        string dayName = day.Page.Title.Substring(ind + 1).Trim();
                        string sublink =
                            HttpUtility.UrlEncode(section.Title.Trim().Replace(' ', '_').Replace("[[", "").Replace(
                                "]]", ""));
                        MatchCollection mc = encSymbRe.Matches(sublink);
                        sublink = mc.Cast<Match>().Aggregate(sublink,
                                                             (current, m) =>
                                                             current.Replace(m.Value, "." + m.Groups[1].Value.ToUpper()));

                        DisputedVerdict newPriorVerdict = new DisputedVerdict();
                        newPriorVerdict.Title = section.Title.Trim();
                        newPriorVerdict.NominationTitle =
							string.Format("[[{0}#{1}|{2}]]", 
								day.Page.Title, 
								sublink.Replace("<s>", "").Replace("</s>", "").Trim (new[]{ ' ', '[', ']', ':' }), 
								dayName);
                        newPriorVerdict.VerdictDate = verdictDate;
						newPriorVerdict.VerdictAuthor = verdictAuthor;
                        _priorVerdicts.Add(newPriorVerdict);
                    }

                    if (hasVerdict || hasAutoVerdict)
                    {
						string verdictText = "";
                        DateTime verdictDate;
                        var dispSection =
                            section.Subsections.First(s => s.Title.ToLower().Trim() == "итог" || 
                                s.Title.ToLower().Trim() == "автоматический итог" ||
                                s.Title.ToLower().Trim() == "автоитог");
                        var timeMatch = timeRE.Match(dispSection.Text);
                        if (timeMatch.Success)
                        {
                            DateTime.TryParse(timeMatch.Groups[1].Value.Trim(), 
								CultureInfo.CreateSpecificCulture("ru-RU"),
								DateTimeStyles.AssumeUniversal,
								out verdictDate);
							verdictText = dispSection.Text.Remove (timeMatch.Index);
                        }
                        else
                        {
                            verdictDate = GetLastUpdateDate(wiki, day.Page.Title);
                        }
						var verdictAuthor = GetVerdictAuthor (verdictText);

                        if (DateTime.UtcNow.Subtract(verdictDate).TotalDays > 14)
                            continue;

                        int ind = day.Page.Title.IndexOf("/");
                        string dayName = day.Page.Title.Substring(ind + 1).Trim();
                        string sublink =
                            HttpUtility.UrlEncode(section.Title.Trim().Replace(' ', '_').Replace("[[", "").Replace(
                                "]]", ""));
                        MatchCollection mc = encSymbRe.Matches(sublink);
                        sublink = mc.Cast<Match>().Aggregate(sublink,
                                                             (current, m) =>
                                                             current.Replace(m.Value, "." + m.Groups[1].Value.ToUpper()));

                        DisputedVerdict newNewVerdict = new DisputedVerdict();
                        newNewVerdict.Title = section.Title.Trim();
                        newNewVerdict.NominationTitle =
							string.Format("[[{0}#{1}|{2}]]", 
								day.Page.Title, 
								sublink.Replace("<s>", "").Replace("</s>", "").Trim (new[]{ ' ', '[', ']', ':' }), 
								dayName);
                        newNewVerdict.NominationDate = day.Date;
                        newNewVerdict.VerdictDate = verdictDate;
						newNewVerdict.VerdictAuthor = verdictAuthor;
                        _newVerdicts.Add(newNewVerdict);
                    }

                    List<WikiPageSection> sections = new List<WikiPageSection>();
                    section.Reduce(sections, SubsectionsList);
                    foreach (WikiPageSection subsection in sections)
                    {
                        hasVerdict = subsection.Subsections.Count(s => s.Title.ToLower().Trim() == "итог") > 0;
                        hasAutoVerdict = subsection.Subsections.Count(s => s.Title.Trim() == "Автоматический итог" || s.Title.Trim() == "Автоитог") > 0;
                        hasDisputedVerdict =
                            subsection.Subsections.Count(s => s.Title.ToLower().Trim() == "оспоренный итог" ||
                                                           s.Title.ToLower().Trim() == "оспореный итог") > 0;
                        if (hasDisputedVerdict)
                            Console.WriteLine(section.Title);

                        if (!hasDisputedVerdict ||
                            (hasDisputedVerdict && (hasVerdict || hasAutoVerdict)))
                            continue;

                        var dispSection =
                            subsection.Subsections.First(s => s.Title.ToLower().Trim() == "оспоренный итог" ||
                                                              s.Title.ToLower().Trim() == "оспореный итог");
                        var timeMatch = timeRE.Match(dispSection.Text);
						string verdictText = "";
                        DateTime verdictDate;
                        if (timeMatch.Success)
                        {
                            DateTime.TryParse(timeMatch.Groups[1].Value.Trim(), 
								CultureInfo.CreateSpecificCulture("ru-RU"),
								DateTimeStyles.AssumeUniversal,
								out verdictDate);
							verdictText = dispSection.Text.Remove (timeMatch.Index);
                        }
                        else
                        {
                            verdictDate = GetLastUpdateDate(wiki, day.Page.Title);
                        }
                        DateTime disputedDate = GetLastUpdateDate(wiki, day.Page.Title);
						var verdictAuthor = GetVerdictAuthor (verdictText);

                        var dayStatus = "Закрыта";
                        if (openDays.Contains(day)) dayStatus = "Открыта";
                        var ind = day.Page.Title.IndexOf("/");
                        var dayName = day.Page.Title.Substring(ind + 1).Trim();
                        var sublink =
                            HttpUtility.UrlEncode(subsection.Title.Trim().Replace(' ', '_').Replace("[[", "").Replace("]]", ""));

                        MatchCollection mc = encSymbRe.Matches(sublink);
                        sublink = mc.Cast<Match>().Aggregate(sublink, (current, m) => current.Replace(m.Value, "." + m.Groups[1].Value.ToUpper()));

                        DisputedVerdict newSubDisputedVerdict = new DisputedVerdict();
                        newSubDisputedVerdict.Title = subsection.Title.Trim();
                        newSubDisputedVerdict.NominationTitle =
							string.Format("[[{0}#{1}|{2}]]", 
								day.Page.Title, 
								sublink.Replace("<s>", "").Replace("</s>", "").Trim (new[]{ ' ', '[', ']', ':' }), 
								dayName);
                        newSubDisputedVerdict.VerdictDate = verdictDate;
                        newSubDisputedVerdict.DisputedDate = disputedDate;
                        newSubDisputedVerdict.NominationPageType = dayStatus;
						newSubDisputedVerdict.VerdictAuthor = verdictAuthor;
                        _newDisputedVerdicts.Add(newSubDisputedVerdict);
                    }
                }
            }

            foreach (var disputedVerdict in _newDisputedVerdicts)
            {
                var oldVerdicts = _oldDisputedVerdicts.Where(dv => dv.NominationTitle == disputedVerdict.NominationTitle).ToList();
                if (!oldVerdicts.Any())
                    continue;
                disputedVerdict.DisputedDate = oldVerdicts[0].DisputedDate;
                disputedVerdict.VerdictDate = oldVerdicts[0].VerdictDate;
            }

            using (StreamWriter sw = new StreamWriter(_cacheDir + "Disputed.txt"))
            {
                sw.WriteLine("{{/Шапка}}\n");
				sw.WriteLine("{| class='wikitable sortable' style='text-align:left;'\n! Номинация \n! Обсуждение\n! Подведён<br/>итог\n! Страница<br/>обсуждения\n! Автор итога");
                foreach (var dv in _newDisputedVerdicts)
                {
					sw.WriteLine("|-\n| {0} || {1} || {2:yyyy.MM.dd} || {3} || {4}",
                        dv.Title,
                        dv.NominationTitle,
                        dv.VerdictDate,                        
                        dv.NominationPageType,
						dv.VerdictAuthor
                        );
                }

                sw.WriteLine("|}\n[[Категория:Википедия:Переименование страниц]]");
            }

            _priorVerdicts = _priorVerdicts.OrderByDescending(v => v.VerdictDate).ThenBy(v => v.DisputedDate).ToList();
            using (StreamWriter sw = new StreamWriter(_cacheDir + "Prior.txt"))
            {
                sw.WriteLine("{{/Шапка}}\n");
				sw.WriteLine("{| class='wikitable sortable' style='text-align:left;'\n! Номинация \n! Обсуждение\n! Подведён предварительный<br/>итог\n! Автор итога");
                foreach (var dv in _priorVerdicts)
                {
					sw.WriteLine("|-\n| {0} || {1} || {2:yyyy.MM.dd} || {3}",
                        dv.Title,
                        dv.NominationTitle,
                        dv.VerdictDate,
						dv.VerdictAuthor
                        );
                }

                sw.WriteLine("|}\n[[Категория:Википедия:Переименование страниц]]");
            }

            _newVerdicts = _newVerdicts.OrderByDescending(v => v.VerdictDate).ThenBy(v => v.DisputedDate).ToList();
            using (StreamWriter sw = new StreamWriter(_cacheDir + "New.txt"))
            {
                sw.WriteLine("{{/Шапка}}\n");
				sw.WriteLine("{| class='wikitable sortable' style='text-align:left;'\n! Номинация \n! Обсуждение\n! Подведён итог\n! Автор итога");
                foreach (var dv in _newVerdicts)
                {
					sw.WriteLine("|-\n| {0} || {1} || {2:yyyy.MM.dd} || {3}",
                        dv.Title.Replace("<s>", "").Replace("</s>", ""),
                        dv.NominationTitle,
                        dv.VerdictDate,
						dv.VerdictAuthor
                        );
                }

                sw.WriteLine("|}\n[[Категория:Википедия:Переименование страниц]]");
            }

			
			using (StreamWriter sw = new StreamWriter (_cacheDir + "MainPage.txt")) {
				sw.WriteLine ("{{/Шапка}}\n");
                sw.WriteLine("{{#invoke:RequestTable|TableByDate|header=Статьи, вынесенные на переименование|link=Википедия:К переименованию\n");
				
				Regex wikiLinkRE = new Regex (@"\[{2}(.+?)(\|.+?)?]{2}");
				
				foreach (Day day in openDays) {
					Console.Out.WriteLine ("Analyzing " + day.Date.ToString ("d MMMM yyyy") + "...");
                    int daysFromNow = (int)Math.Floor(DateTime.UtcNow.Subtract(day.Date).TotalDays);
                    sw.Write("\n|" + day.Date.ToString("yyyy-M-d") + "|\n");
					List<string> titles = new List<string> ();
					foreach (WikiPageSection section in day.Page.Sections) {
						string filler = "";
						string result = "";
						RemoveStrikeOut (section);
						StrikeOutSection (section);
                        var isStriked = CheckStrike(section);
						
						if (section.SectionText.ToLower().Contains ("{{mark out}}") ||
                            section.SectionText.ToLower().Contains("{{сложное обсуждение}}"))
                        {
							section.Title = "{{mark out|" + section.Title.Trim () + "}}";
						}

                        var subsections = section.Subsections;
                        if (subsections != null
                            && subsections.All(sss => sss.Title.Trim().ToLower() != "итог")
                            && subsections.All(sss => sss.Title.Trim().ToLower() != "автоматический итог")
                            && subsections.All(sss => sss.Title.Trim().ToLower() != "оспоренный итог")
                            && subsections.Any(sss => sss.Title.Trim().ToLower() == "предварительный итог"))
                        {
                            section.Title = "{{Предварительный итог|" + section.Title.Trim() + "}}";
                        }                        
                        if (subsections != null
                            && subsections.All(sss => sss.Title.Trim().ToLower() != "итог")
                            && subsections.All(sss => sss.Title.Trim().ToLower() != "автоматический итог")
                            && subsections.Any(sss => sss.Title.Trim().ToLower() == "оспоренный итог"))
                        {
                            section.Title = "{{Оспоренный итог|" + section.Title.Trim() + "}}";
                        }
                        bool isNewVerdict = false;
                        if (_newVerdicts.Any(v => v.NominationDate == day.Date && section.Title.Contains(v.Title))) 
                        {
                            section.Title = "{{Новый итог|" + section.Title.Trim() + "}}";
                            isNewVerdict = true;
                        }
                      
						
						bool hasVerdict = section.Subsections.Count (s => s.Title.ToLower ().Trim () == "итог") > 0;
						bool hasAutoVerdict = section.Subsections.Count (s => s.Title.Trim () == "Автоматический итог") > 0;
						if (hasVerdict || hasAutoVerdict || section.Title.Contains ("<s>")) {
							Match m = wikiLinkRE.Match (section.Title);
							if (m.Success && !m.Groups[1].Value.StartsWith (":Категория:")) {
								string link = m.Groups[1].Value;
								string movedTo;
								bool moved = MovedTo (wiki, link, day.Date, out movedTo);
								
								if (moved && string.IsNullOrEmpty (movedTo)) {
									result = " ''(переименовано)''";
								} else if (moved) {
									result = string.Format (" ''({1}переименовано в «[[{0}]]»)''", movedTo.StartsWith ("Файл:") ? ":" + movedTo : movedTo, hasAutoVerdict ? "де-факто " : "");
								} else {
									result = " ''(не переименовано)''";
								}
							}
						}
						
						for (int i = 0; i < section.Level - 1; ++i) {
							filler += "*";
						}
                        if(!isStriked || daysFromNow < 90 || isNewVerdict)
                            titles.Add (filler + " " + section.Title.Trim () + result);
						
						List<WikiPageSection> sections = new List<WikiPageSection> ();
						section.Reduce (sections, SubsectionsList);
						foreach (WikiPageSection subsection in sections) {
                            if (subsection.SectionText.ToLower().Contains("{{mark out}}") ||
                            section.SectionText.ToLower().Contains("{{сложное обсуждение}}"))
                            {
								subsection.Title = "{{mark out|" + subsection.Title.Trim () + "}}";
							}

						    var subsubsections = subsection.Subsections;
                            if(subsubsections != null 
                                && subsubsections.All(sss => sss.Title.ToLower() != "итог")
                                && subsubsections.All(sss => sss.Title.ToLower() != "автоматический итог")
                                && subsubsections.All(sss => sss.Title.ToLower() != "оспоренный итог")
                                && subsubsections.Any(sss => sss.Title.ToLower() == "предварительный итог"))
                            {
                                subsection.Title = "{{Предварительный итог|" + subsection.Title.Trim() + "}}";
                            }
                            if (subsubsections != null
                            && subsubsections.All(sss => sss.Title.Trim().ToLower() != "итог")
                            && subsubsections.All(sss => sss.Title.Trim().ToLower() != "автоматический итог")
                            && subsubsections.Any(sss => sss.Title.Trim().ToLower() == "оспоренный итог"))
                            {
                                section.Title = "{{Оспоренный итог|" + section.Title.Trim() + "}}";
                            }
                            


						    result = "";
							hasVerdict = subsection.Subsections.Count (s => s.Title.ToLower ().Trim () == "итог") > 0;
							hasAutoVerdict = subsection.Subsections.Count (s => s.Title.Trim () == "Автоматический итог") > 0;
							if (hasVerdict || hasAutoVerdict || subsection.Title.Contains ("<s>")) {
								Match m = wikiLinkRE.Match (subsection.Title);
								if (m.Success && !m.Groups[1].Value.StartsWith (":Категория:")) {
									string link = m.Groups[1].Value;
									string movedTo;
									bool moved = MovedTo (wiki, link, day.Date, out movedTo);
									
									if (moved && string.IsNullOrEmpty (movedTo)) {
										result = " ''(переименовано)''";
									} else if (moved) {
										result = string.Format (" ''({1}переименовано в «[[{0}]]»)''", movedTo.StartsWith ("Файл:") ? ":" + movedTo : movedTo, hasAutoVerdict ? "де-факто " : "");
									} else {
										result = " ''(не переименовано)''";
									}
								}
							}
							filler = "";
							for (int i = 0; i < subsection.Level - 1; ++i) {
								filler += "*";
							}
                            isStriked = CheckStrike(subsection);
                            if (!isStriked || daysFromNow < 90)
                                titles.Add(filler + " " + subsection.Title.Trim() + result);
						}
					}
                    sw.Write (string.Join ("\n", titles.ConvertAll (c => c).ToArray ()));
				}
				
                sw.WriteLine("}}");
                sw.WriteLine("{{/Окончание}}");
			}
             
		}

		private string GetVerdictAuthor(string text)
		{
			Regex userRE = new Regex(@"\[\[\s*(User:|U:|Участник:|Участница:|У:|User talk:|UT:|Обсуждение участника:|Обсуждение участницы:|ОУ:)\s*([^\]\|#]+)\s*[\]\|#]", RegexOptions.IgnoreCase);
			Regex anonRE = new Regex(@"\[\[\Special:Contributions/([^\]\|#]+)\s*[\]\|#]");
			int userPos = -1;
			string verdictAuthor = "";
			string fullAuthorString = "";
			int userType = -1;
			var userMatches = userRE.Matches (text);
			foreach(Match m in userMatches)
			{				
				if (m.Index > userPos) 
				{
					userPos = m.Index;
					verdictAuthor = m.Groups [2].Value.Trim ();
					userType = 1;
					fullAuthorString = string.Format ("[[Участник:{0}|{0}]]", verdictAuthor);
				}
			}
			var anonMatches = anonRE.Matches (text);
			foreach(Match m in anonMatches)
			{

				if (m.Index > userPos) 
				{
					userPos = m.Index;
					verdictAuthor = m.Groups [1].Value.Trim ();
					userType = 0;
					fullAuthorString = string.Format ("[[Special:Contributions/{0}|{0}]]", verdictAuthor);
				}
			}

			return fullAuthorString;
		}

        private void UrgencyCheck(Wiki wiki, XmlNodeList pages, List<Day> days)
        {
            Regex closedRE = new Regex(@"(\{{2}КПМ-Навигация\}{2}\s*\{{2}(Закрыто|Closed|закрыто|closed)\}{2})|(\{{2}(Закрыто|Closed|закрыто|closed)\}{2}\s*\{{2}КПМ-Навигация\}{2})");
			
            foreach (XmlNode page in pages)
            {
                string pageName = page.Attributes["title"].Value;
                string date = pageName.Substring("Википедия:К переименованию/".Length);
                Day day = new Day();
                try
                {
                    day.Date = DateTime.Parse(RuDateTime.MonthNormalize(date), CultureInfo.CreateSpecificCulture("ru-RU"), DateTimeStyles.AssumeUniversal);
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
                DateTime lastEdit = DateTime.Parse(page.FirstChild.FirstChild.Attributes["timestamp"].Value, null, DateTimeStyles.AssumeUniversal);
                Match m = closedRE.Match(text);
                if ((DateTime.Now - lastEdit).TotalDays > 2 && m.Success)
                {
                    text = text.Replace("{{КПМ-Навигация}}", "{{КПМ-Навигация|nocat=1}}");
                    try
                    {
                        string revid = wiki.Save(pageName, text, "обсуждение закрыто");

                        using (FileStream fs = new FileStream(fileName, FileMode.Create))
                        using (GZipStream gs = new GZipStream(fs, CompressionMode.Compress))
                        using (StreamWriter sw = new StreamWriter(gs))
                        {
                            sw.WriteLine(revid);
                            sw.Write(text);
                        }
                    }
                    catch (WikiException)
                    {
                    }
                    continue;
                }
                day.Page = WikiPage.Parse(pageName, text);
                days.Add(day);
            }
        }

	    public void UpdateMainPage (Wiki wiki)
		{
			Console.Out.WriteLine ("Updating requested moves...");
			using (TextReader sr = new StreamReader (_cacheDir + "MainPage.txt")) {
				string text = sr.ReadToEnd ();
                Save(wiki, "Википедия:К переименованию", text, "обновление");				
				
			}
            Console.Out.WriteLine("Updating disputed verdicts...");
            using (TextReader sr = new StreamReader(_cacheDir + "Disputed.txt"))
            {
                string text = sr.ReadToEnd();
                Save(wiki, "Википедия:К переименованию/Оспоренные итоги", text, "обновление");
                 
            }
            Console.Out.WriteLine("Updating prior verdicts...");
            using (TextReader sr = new StreamReader(_cacheDir + "Prior.txt"))
            {
                string text = sr.ReadToEnd();
                Save(wiki, "Википедия:К переименованию/Предварительные итоги", text, "обновление");	                  
            }
            Console.Out.WriteLine("Updating new verdicts...");
            using (TextReader sr = new StreamReader(_cacheDir + "New.txt"))
            {
                string text = sr.ReadToEnd();
                Save(wiki, "Википедия:К переименованию/Новые итоги", text, "обновление");
            }
		}

		public void UpdatePages (Wiki wiki)
		{
			Regex closedRE = new Regex (@"(\{{2}КПМ-Навигация\}{2}\s*\{{2}(Закрыто|Closed|закрыто|closed)\}{2})|(\{{2}(Закрыто|Closed|закрыто|closed)\}{2}\s*\{{2}КПМ-Навигация\}{2})");
			Regex wikiLinkRE = new Regex (@"\[{2}(.+?)(\|.+?)?]{2}");
			Regex timeRE = new Regex (@"(\d{2}:\d{2}\, \d\d? [а-я]+ \d{4}) \(UTC\)");
			Regex stopAVRE = new Regex (@"\{\{\s*(Не подводить автоитог|не подводить автоитог|Не подводить автоматический итог|не подводить автоматический итог|Автоитог не нужен|автоитог не нужен|Не нужно автоитога|не нужно автоитога)\s*\}\}");
			
			ParameterCollection parameters = new ParameterCollection ();
			parameters.Add ("generator", "categorymembers");
			parameters.Add ("gcmtitle", "Категория:Википедия:Незакрытые обсуждения переименования страниц");
            parameters.Add ("gcmlimit", "max");
			parameters.Add ("gcmnamespace", "4");
			parameters.Add ("prop", "info|revisions");
			parameters.Add ("intoken", "edit");
			XmlDocument doc = wiki.Enumerate (parameters, true);
			string queryTimestamp = DateTime.Now.ToUniversalTime ().ToString ("yyyy-MM-ddTHH:mm:ssZ");
			XmlNodeList pages = doc.SelectNodes ("//page");
			foreach (XmlNode page in pages) {
				int results = 0;
				string pageName = page.Attributes ["title"].Value;
				string date = pageName.Substring ("Википедия:К переименованию/".Length);
				string basetimestamp = page.FirstChild.FirstChild.Attributes ["timestamp"].Value;
				string editToken = page.Attributes ["edittoken"].Value;
				
				Day day = new Day ();
				if (!DateTime.TryParse (RuDateTime.MonthNormalize (date), CultureInfo.CreateSpecificCulture ("ru-RU"), DateTimeStyles.AssumeUniversal, out day.Date)) {
					continue;
				}

				if (day.Date.Subtract (new DateTime (2010, 8, 29)).Days < 0)
					continue;
                
				string fileName = _cacheDir + date + ".bin";
				string text = "";
				if (File.Exists (fileName)) {
					using (FileStream fs = new FileStream (fileName, FileMode.Open))
					using (GZipStream gs = new GZipStream (fs, CompressionMode.Decompress))
					using (TextReader sr = new StreamReader (gs)) {
						string revid = sr.ReadLine ();
						if (revid == page.Attributes ["lastrevid"].Value) {
							Console.Out.WriteLine ("Loading " + pageName + "...");
							text = sr.ReadToEnd ();
						}
					}
				}
				if (string.IsNullOrEmpty (text)) {
					Console.Out.WriteLine ("Downloading " + pageName + "...");
					int it = 0;
					while (true) {
						try {
                            text = wiki.LoadTextRev (pageName);
							break;
						} catch (WikiException) {
							Console.WriteLine ("Iteration {0}", it);
							System.Threading.Thread.Sleep (10000);
							it++;
							if (it > 5)
								throw;
						}
					}
					using (FileStream fs = new FileStream (fileName, FileMode.Create))
					using (GZipStream gs = new GZipStream (fs, CompressionMode.Compress))
					using (StreamWriter sw = new StreamWriter (gs)) {
						sw.WriteLine (page.Attributes ["lastrevid"].Value);
						sw.Write (text);
					}
				}                
				day.Page = WikiPage.Parse (pageName, text);                
				
				List<string> titlesWithResults = new List<string> ();
				foreach (WikiPageSection section in day.Page.Sections) {
					RemoveStrikeOut (section);
					StrikeOutSection (section);
				    
					
					Match m = wikiLinkRE.Match (section.Title);
					
					m = wikiLinkRE.Match (section.Title);
					if (m.Success && section.Title.Contains ("<s>")) {
						DateTime verdictTime;
						var verdictSections = section.Subsections.Where (
							                         s => s.Title.ToLower ().Trim () == "итог" || s.Title.Trim () == "Автоматический итог").ToList ();
						if (verdictSections.Count > 0) {
							var md = timeRE.Match (verdictSections.Last ().SectionText);
							if (md.Success) {
								verdictTime = DateTime.Parse (RuDateTime.MonthNormalize (md.Groups [1].Value), CultureInfo.CreateSpecificCulture ("ru-RU"), DateTimeStyles.AssumeUniversal);
								if (DateTime.UtcNow.Subtract (verdictTime).TotalDays > 30)
									titlesWithResults.Add (m.Groups [1].Value);
							}
						}
					}
					List<WikiPageSection> sections = new List<WikiPageSection> ();
					section.Reduce (sections, SubsectionsList);
					foreach (WikiPageSection subsection in sections) {
						m = wikiLinkRE.Match (subsection.Title);
						if (m.Success && subsection.Title.Contains ("<s>")) {
							titlesWithResults.Add (m.Groups [1].Value.Trim ());
						}
					}
				}
				
				Match matchClosed = closedRE.Match (text);
				{
					List<TalkResult> talkResults = new List<TalkResult> ();
					foreach (string name in titlesWithResults) {
						if (wiki.PageNamespace (name) > 0) {
							continue;
						}
						string movedTo;
						string movedBy;
						DateTime movedAt;
						
						bool moved = MovedTo (wiki, name, day.Date, out movedTo, out movedBy, out movedAt);
						talkResults.Add (new TalkResult (name, movedTo, moved));
					}
					
					parameters.Clear ();
					parameters.Add ("prop", "info");
					XmlDocument xml = wiki.Query (QueryBy.Titles, parameters, talkResults.ConvertAll (r => r.Moved ? r.MovedTo : r.Title));
					List<string> notificationList = new List<string> ();
					foreach (XmlNode node in xml.SelectNodes ("//page")) {
						if (node.Attributes["invalid"] == null && node.Attributes ["missing"] == null && node.Attributes ["ns"].Value == "0") {
							notificationList.Add (node.Attributes ["title"].Value);
						}
					}
					if (notificationList.Count > 0) {
						parameters.Clear ();
						parameters.Add ("list", "backlinks");
						parameters.Add ("bltitle", pageName);
						parameters.Add ("blfilterredir", "nonredirects");
						parameters.Add ("blnamespace", "0|1");
						parameters.Add ("bllimit", "max");
						
						XmlDocument backlinks = wiki.Enumerate (parameters, true);
						foreach (string title in notificationList) {
                                if (backlinks.SelectSingleNode ("//bl[@title=\"" + title.Replace ("\"", "&quote;") + "\"]") == null &&
							                         backlinks.SelectSingleNode ("//bl[@title=\"Обсуждение:" + title.Replace ("\"", "&quote;") + "\"]") == null) {
								TalkResult tr = talkResults.Find (r => r.Moved ? r.MovedTo == title : r.Title.ToLower () == title.ToLower ());
								PutNotification (wiki, tr, day.Date);
							}
						}
					}
				}
				
				string newText = day.Page.Text;
				if (newText.Trim () == text.Trim ()) {
					continue;
				}
				try {
					Console.Out.WriteLine ("Updating " + pageName + "...");
					string revid = wiki.Save (pageName, "", newText, "зачёркивание заголовков" + (results > 0 ? ", сообщение об итогах" : ""), MinorFlags.Minor, CreateFlags.NoCreate, WatchFlags.None, SaveFlags.Replace, true, basetimestamp,
						               "", editToken);
					
					using (FileStream fs = new FileStream (fileName, FileMode.Create))
					using (GZipStream gs = new GZipStream (fs, CompressionMode.Compress))
					using (StreamWriter sw = new StreamWriter (gs)) {
						sw.WriteLine (revid);
						sw.Write (newText);
					}
				} catch (WikiException) {
				}
			}
		}

		private void PutNotification (Wiki wiki, TalkResult result, DateTime date)
		{
            if(result == null)
                return;
            if (CheckArticleState(wiki, result.Moved ? result.MovedTo : result.Title) != 0)
                return;
			string talkPageTemplate;
			string dateString = date.ToString ("d MMMM yyyy", CultureInfo.CreateSpecificCulture ("ru-RU"));
			if (!result.Moved) {
				talkPageTemplate = "{{Не переименовано|" + dateString + "|" + result.Title + "}}\n";
			} else {
				talkPageTemplate = "{{Переименовано|" + dateString + "|" + result.Title + "|" + result.MovedTo + "}}\n";
			}
			
			string talkPage = "Обсуждение:" + (result.Moved ? result.MovedTo : result.Title);
			Console.Out.WriteLine ("Updating " + talkPage + "...");
			try {
				ParameterCollection parameters = new ParameterCollection ();
				parameters.Add ("rvprop", "content");
				parameters.Add ("rvsection", "0)");
				parameters.Add ("prop", "revisions");
				XmlDocument xml = wiki.Query (QueryBy.Titles, parameters, new string[] { talkPage });
				string content;
				XmlNode node = xml.SelectSingleNode ("//rev");
				if (node != null) {
					content = node.FirstChild != null ? node.FirstChild.Value : "";
				} else {
					content = "";
				}
				
				int index = content.IndexOf ("{{talkheader", StringComparison.CurrentCultureIgnoreCase);
				if (index != -1) {
					int endIndex = content.IndexOf ("}}", index);
					if (endIndex != -1) {
						content = content.Insert (endIndex + 2, "\n" + talkPageTemplate);
					}
				} else {
					index = content.IndexOf ("{{заголовок обсуждения", StringComparison.CurrentCultureIgnoreCase);
					if (index != -1) {
						int endIndex = content.IndexOf ("}}", index);
						if (endIndex != -1) {
							content = content.Insert (endIndex + 2, "\n" + talkPageTemplate);
						}
					} else {
						content = content.Insert (0, talkPageTemplate);
					}
				}
				
				SaveSection (wiki, talkPage, "0", content, "итог");
			} catch (WikiException e) {
				Console.Out.WriteLine ("Failed to update " + talkPage + ":" + e.Message);
			}
		}

		public void UpdateArchivePages (Wiki wiki, int year, int monthNumber)
		{
			Regex wikiLinkRE = new Regex (@"\[{2}(.+?)(\|.+?)?]{2}");
			
			DateTime month = new DateTime (year, monthNumber, 1);
			DateTime end = month.AddMonths (1);
			using (StreamWriter archiveSW = new StreamWriter (_cacheDir + "Archive-" + year.ToString () + "-" + monthNumber.ToString () + ".txt"))
				while (month < end && month < DateTime.Now) {
					DateTime start = month;
					DateTime nextMonth = start.AddMonths (1);
					List<string> titles = new List<string> ();
					while (start < nextMonth) {
						string pageDate = start.ToString ("d MMMM yyyy", CultureInfo.CreateSpecificCulture ("ru-RU"));
						string prefix = "Википедия:К переименованию/";
						string pageName = prefix + pageDate;
						titles.Add (pageName);
						
						start = start.AddDays (1);
					}
					
					ParameterCollection parameters = new ParameterCollection ();
					parameters.Add ("prop", "info");
					
					List<Day> days = new List<Day> ();
					XmlDocument xml = wiki.Query (QueryBy.Titles, parameters, titles);
					XmlNodeList archives = xml.SelectNodes ("//page");
					foreach (XmlNode page in archives) {
						string pageName = page.Attributes["title"].Value;
						string dateString = pageName.Substring ("Википедия:К переименованию/".Length);
						
						string pageFileName = _cacheDir + dateString + ".bin";
						Day day = new Day ();
						
						try {
							day.Date = DateTime.Parse (RuDateTime.MonthNormalize (dateString), CultureInfo.CreateSpecificCulture ("ru-RU"), DateTimeStyles.AssumeUniversal);
						} catch (FormatException) {
							continue;
						}
						
						if (page.Attributes["missing"] != null) {
							day.Exists = false;
							days.Add (day);
							continue;
						}
						
						string text = "";
						if (File.Exists (pageFileName)) {
							using (FileStream fs = new FileStream (pageFileName, FileMode.Open))
								using (GZipStream gs = new GZipStream (fs, CompressionMode.Decompress))
									using (TextReader sr = new StreamReader (gs)) {
										string revid = sr.ReadLine ();
										if (revid == page.Attributes["lastrevid"].Value) {
											Console.Out.WriteLine ("Loading " + pageName + "...");
											text = sr.ReadToEnd ();
										}
									}
						}
						if (string.IsNullOrEmpty (text)) {
							Console.Out.WriteLine ("Downloading " + pageName + "...");
                            text = wiki.LoadTextRev(pageName);
							using (FileStream fs = new FileStream (pageFileName, FileMode.Create))
								using (GZipStream gs = new GZipStream (fs, CompressionMode.Compress))
									using (StreamWriter sw = new StreamWriter (gs)) {
										sw.WriteLine (page.Attributes["lastrevid"].Value);
										sw.Write (text);
									}
						}
						day.Exists = true;
						day.Page = WikiPage.Parse (pageName, text);
						days.Add (day);
					}
					
					days.Sort (CompareDays);
					
					StringBuilder textBuilder = new StringBuilder ();
					textBuilder.AppendLine ("{{Навигация по архиву КПМ}}\n{{Переименование статей/Начало}}");
					
					StringBuilder sb = new StringBuilder ();
					foreach (Day day in days) {
						
						sb.Append ("{{Переименование статей/День|" + day.Date.ToString ("yyyy-MM-dd") + "|\n");
						if (!day.Exists) {
							sb.Append ("''нет обсуждений''}}\n\n");
							continue;
						}
						titles.Clear ();
						Console.Out.WriteLine ("Analyzing " + day.Date.ToString ("d MMMM yyyy") + "...");
						foreach (WikiPageSection section in day.Page.Sections) {
							string filler = "";
							string result = "";
							bool hasVerdict = section.Subsections.Count (s => s.Title.ToLower ().Trim () == "итог") > 0;
							bool hasAutoVerdict = section.Subsections.Count (s => s.Title.Trim () == "Автоматический итог") > 0;
							if (hasVerdict || hasAutoVerdict || section.Title.Contains ("<s>")) {
								Match m = wikiLinkRE.Match (section.Title);
								if (m.Success && !m.Groups[1].Value.StartsWith (":Категория:")) {
									string link = m.Groups[1].Value;
									string movedTo;
									bool moved = MovedTo (wiki, link, day.Date, out movedTo);
									
									if (moved && string.IsNullOrEmpty (movedTo)) {
										result = " ''(переименовано)''";
									} else if (moved) {
										result = string.Format (" ''({1}переименовано в «[[{0}]]»)''", movedTo.StartsWith ("Файл:") ? ":" + movedTo : movedTo, hasAutoVerdict && !hasVerdict ? "де-факто " : "");
									} else {
										result = " ''(не переименовано)''";
									}
								}
							}
							
							for (int i = 0; i < section.Level - 1; ++i) {
								filler += "*";
							}
							titles.Add (filler + " " + section.Title.Trim () + result);
							
							List<WikiPageSection> sections = new List<WikiPageSection> ();
							section.Reduce (sections, SubsectionsList);
							foreach (WikiPageSection subsection in sections) {
								result = "";
								hasVerdict = subsection.Subsections.Count (s => s.Title.ToLower ().Trim () == "итог") > 0;
								hasAutoVerdict = subsection.Subsections.Count (s => s.Title.Trim () == "Автоматический итог") > 0;
								if (hasVerdict || hasAutoVerdict || subsection.Title.Contains ("<s>")) {
									Match m = wikiLinkRE.Match (subsection.Title);
									if (m.Success && !m.Groups[1].Value.StartsWith (":Категория:")) {
										string link = m.Groups[1].Value;
										string movedTo;
										bool moved = MovedTo (wiki, link, day.Date, out movedTo);
										
										if (moved && string.IsNullOrEmpty (movedTo)) {
											result = " ''(переименовано)''";
										} else if (moved) {
											result = string.Format (" ''({1}переименовано в «[[{0}]]»)''", movedTo.StartsWith ("Файл:") ? ":" + movedTo : movedTo, hasAutoVerdict && !hasVerdict ? "де-факто " : "");
										} else {
											result = " ''(не переименовано)''";
										}
									}
								}
								filler = "";
								for (int i = 0; i < subsection.Level - 1; ++i) {
									filler += "*";
								}
								titles.Add (filler + " " + subsection.Title.Trim () + result);
							}
						}
						if (titles.Count (s => s.Contains ("=")) > 0) {
							titles[0] = "2=<li>" + titles[0].Substring (2) + "</li>";
						}
						sb.Append (string.Join ("\n", titles.ConvertAll (c => c).ToArray ()));
						sb.Append ("}}\n\n");
					}
					sb.Replace ("<s>", "");
					sb.Replace ("</s>", "");
					sb.Replace ("<strike>", "");
					sb.Replace ("</strike>", "");
					
					textBuilder.Append (sb.ToString ());
					textBuilder.AppendLine ("{{Переименование статей/Конец}}");
					
					archiveSW.WriteLine (textBuilder.ToString ());
					
					month = month.AddMonths (1);
				}
			
			string archiveName = string.Format ("Википедия:Архив запросов на переименование/{0}-{1:00}", year, monthNumber);
			Console.Out.WriteLine ("Updating " + archiveName + "...");
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    using (TextReader sr = new StreamReader(_cacheDir + "Archive-" + year.ToString() + "-" + monthNumber.ToString() + ".txt"))
                    {
                        string text = sr.ReadToEnd();
                        Console.WriteLine(text.Length);
                        Save(wiki, archiveName, text, "обновление");
                    }
                    break;
                }
                catch (Exception e) 
                {
                    Console.WriteLine(e);
                    if (i == 4)
                        throw;
                    System.Threading.Thread.Sleep(10000);
                }
            }
		}

		internal void AddNavigationTemplate (Wiki wiki)
		{
			ParameterCollection parameters = new ParameterCollection ();
			parameters.Add ("list", "embeddedin");
			parameters.Add ("eititle", "Template:КПМ-Навигация");
			parameters.Add ("eilimit", "max");
			parameters.Add ("einamespace", "4");
			parameters.Add ("eifilterredir", "nonredirects");
			
			XmlDocument doc = wiki.Enumerate (parameters, true);
			
			List<string> titles = new List<string> ();
			DateTime end = DateTime.Today;
			DateTime start = end.AddDays (-7);
			while (start <= end) {
				string pageDate = start.ToString ("d MMMM yyyy", CultureInfo.CreateSpecificCulture ("ru-RU"));
				string prefix = "Википедия:К переименованию/";
				string pageName = prefix + pageDate;
				if (doc.SelectSingleNode ("//ei[@title='" + pageName + "']") == null) {
					titles.Add (pageName);
				}
				start = start.AddDays (1);
			}
			
			parameters.Clear ();
			parameters.Add ("prop", "info");
			XmlDocument xml = wiki.Query (QueryBy.Titles, parameters, titles);
			foreach (XmlNode node in xml.SelectNodes ("//page")) {
				if (node.Attributes["missing"] == null) {
					Console.Out.WriteLine ("Updating " + node.Attributes["title"].Value + "...");
					wiki.Prepend (node.Attributes["title"].Value, "{{КПМ-Навигация}}\n", "добавление навигационного шаблона");
				}
			}
		}

		private static bool MovedTo (Wiki wiki, string title, DateTime start, out string movedTo)
		{
			string movedBy;
			DateTime movedAt;
			return MovedTo (wiki, title, start, out movedTo, out movedBy, out movedAt);
		}

		private static bool MovedTo (Wiki wiki, string title, DateTime start, out string movedTo, out string movedBy, out DateTime movedAt)
		
			ParameterCollection parameters = new ParameterCollection ();
			parameters.Add ("list", "logevents");
			parameters.Add ("letitle", title);
			parameters.Add ("letype", "move");
			parameters.Add ("lestart", start.ToUniversalTime ().ToString ("yyyy-MM-ddTHH:mm:ssZ"));
			parameters.Add ("ledir", "newer");
			parameters.Add ("lelimit", "max");
			XmlDocument doc;
			int it = 0;
			while (true) {
				try {
					doc = wiki.Enumerate (parameters, true);
					break;
				} catch (WikiException) {
					Console.WriteLine ("Iteration {0}", it);
					System.Threading.Thread.Sleep (10000);
					it++;
					if (it > 5)
						throw;
				}
			}

            XmlNodeList moved = doc.SelectNodes("//params");
			List<Revision> revs = new List<Revision> ();
			foreach (XmlNode revision in moved) {
                var typeNode = revision.ParentNode.Attributes["type"];
                var actionNode = revision.ParentNode.Attributes["action"];
                if (typeNode == null || typeNode.Value != "move" ||
                     actionNode == null || (actionNode.Value != "move") && (actionNode.Value != "move_redir"))
                    continue;
                revs.Add(new Revision(revision.Attributes["target_title"].Value, revision.ParentNode.Attributes["comment"].Value, 
                    revision.ParentNode.Attributes["timestamp"].Value, revision.ParentNode.Attributes["user"].Value));
			}
			revs.Sort (CompareRevisions);
			if (revs.Count > 0) {
				bool result = MovedTo (wiki, revs[0].MovedTo, revs[0].Time, out movedTo, out movedBy, out movedAt);
				if (result) {
					return movedTo != title;
				} else {
					movedTo = revs[0].MovedTo;
					movedBy = revs[0].User;
					movedAt = revs[0].Time;
					return movedTo != title;
				}
			}
			movedTo = "";
			movedBy = "";
			movedAt = new DateTime ();
			return false;
		}

		struct Revision
		{
			public string Comment;
			public DateTime Time;
			public string User;
			public string MovedTo;

			public Revision (string movedTo, string comment, string time, string user)
			{
				MovedTo = movedTo;
				Comment = comment;
				Time = DateTime.Parse (time, null, DateTimeStyles.AssumeUniversal);
				User = user;
			}
		}

		private class TalkResult
		{
			public string Title { get; private set; }
			public string MovedTo { get; private set; }
			public bool Moved { get; private set; }

			public TalkResult (string title, string movedTo, bool moved)
			{
				Title = title;
				Moved = moved;
				MovedTo = movedTo;
			}
		}

		static internal int CompareDays (Day x, Day y)
		{
			return y.Date.CompareTo (x.Date);
		}

		static int CompareRevisions (Revision x, Revision y)
		{
			return y.Time.CompareTo (x.Time);
		}

		private void StrikeOutSection (WikiPageSection section)
		{
			Regex wikiLinkRE = new Regex (@"\[{2}(.+?)(\|.+?)?]{2}");
			
			if (section.Subsections.Count (s => s.Title.ToLower ().Trim () == "итог" || s.Title.Trim () == "Автоматический итог") > 0) {
				if (!section.Title.Contains ("<s>")) {
					section.Title = string.Format (" <s>{0}</s> ", section.Title.Trim ());
				}
				
				foreach (WikiPageSection subsection in section.Subsections) {
					Match m = wikiLinkRE.Match (subsection.Title);
					if (m.Success && !subsection.Title.Contains ("<s>")) {
						subsection.Title = string.Format (" <s>{0}</s> ", subsection.Title.Trim ());
					}
				}
			}
			section.ForEach (StrikeOutSection);            
		}

		private void RemoveStrikeOut (WikiPageSection section)
		{
			if (section.Subsections.Count (s => s.Title.ToLower ().Trim () == "итог") == 0 && section.Subsections.Count (s => s.Title.Trim () == "Автоматический итог") == 0) {
				if (section.Title.Contains ("<s>")) {
					section.Title = section.Title.Replace ("<s>", "");
					section.Title = section.Title.Replace ("</s>", "");
				}
			}
			section.ForEach (RemoveStrikeOut);
		}

        private bool CheckStrike(WikiPageSection section)
        {
            if (section.Title.Contains("<s>"))
                return true;
            else
                return false;
        }

		private static List<WikiPageSection> SubsectionsList (WikiPageSection section, List<WikiPageSection> aggregator)
		{
			Regex wikiLinkRE = new Regex (@"\[{2}(.+?)(\|.+?)?]{2}");
			Match m = wikiLinkRE.Match (section.Title);
			if (m.Success) {
				aggregator.Add (section);
			}
			return section.Reduce (aggregator, SubsectionsList);
		}

		private void UpdateArchives (Wiki wiki)
		{
			var parameters = new ParameterCollection { { "generator", "categorymembers" }, { "gcmtitle", "Категория:Википедия:Незакрытые обсуждения переименования страниц" }, { "gcmlimit", "max" }, { "gcmnamespace", "4" }, { "prop", "info" } };
			XmlDocument doc = wiki.Enumerate (parameters, true);
			XmlNodeList pages = doc.SelectNodes ("//page");
			
			DateTime startDate = DateTime.Today;
			foreach (XmlNode page in pages) {
				string pageName = page.Attributes["title"].Value;
				string dateString = pageName.Substring ("Википедия:К переименованию/".Length);
				
				DateTime date;
				if (!DateTime.TryParse (RuDateTime.MonthNormalize (dateString), CultureInfo.CreateSpecificCulture ("ru-RU"), DateTimeStyles.AssumeUniversal, out date)) {
					continue;
				}
				if (date < startDate) {
					startDate = new DateTime (date.Year, date.Month, 1);
				}
			}
			
			DateTime month = new DateTime (DateTime.Today.Year, DateTime.Today.Month, 1);
			do {
                UpdateArchivePages (wiki, month.Year, month.Month);
				month = month.AddMonths (-1);
			} while (month != startDate);
		}

        private DateTime GetLastUpdateDate(Wiki wiki, string title)
        {
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("titles", title);
            parameters.Add("prop", "revisions");
            parameters.Add("rvprop", "timestamp");

            XmlDocument doc;
            int it = 0;
            while (true)
            {
                try
                {
                    doc = wiki.Enumerate(parameters, true);
                    break;
                }
                catch (WikiException)
                {
                    Console.WriteLine("Iteration {0}", it);
                    System.Threading.Thread.Sleep(10000);
                    it++;
                    if (it > 5)
                        throw;
                }
            }

            XmlNodeList revisions = doc.SelectNodes("//rev");
            if (revisions.Count < 1)
                return DateTime.UtcNow;
            string timestamp = revisions[0].Attributes["timestamp"].Value;
            DateTime edTime;
            var res = DateTime.TryParse(RuDateTime.MonthNormalize(timestamp), null, DateTimeStyles.AssumeUniversal, out edTime);
            return res ? edTime : DateTime.UtcNow;
        }

        private void GetOldDisputedResults(Wiki wiki)
        {
            string oldText = "";
            _oldDisputedVerdicts = new List<DisputedVerdict>();
            try
            {
                oldText = wiki.LoadTextRev("Википедия:К переименованию/Оспоренные итоги");
            }
            catch (Exception)
            {
                Console.WriteLine("Unable to load disputed results page");
                return;
            }
            var strs = oldText.Split(new[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
            foreach (var str in strs)
            {
                var items = str.Split(new[] {"||"}, StringSplitOptions.None);
                if(items.Length < 5)
                    continue;
                DisputedVerdict newDisputedVerdict = new DisputedVerdict();
                newDisputedVerdict.Title = items[0].Trim(' ', '|');
                newDisputedVerdict.NominationTitle = items[1].Trim();
                string resultDateString = items[2].Trim();
                string disputedDateString = items[3].Trim();
                newDisputedVerdict.NominationPageType = items[4].Trim();
                var res =
                    DateTime.TryParseExact(resultDateString,
                                           "yyyy.MM.dd",
                                           null,
                                           DateTimeStyles.None,
                                           out newDisputedVerdict.VerdictDate);
                res =
                    DateTime.TryParseExact(disputedDateString,
                                           "yyyy.MM.dd",
                                           null,
                                           DateTimeStyles.None,
                                           out newDisputedVerdict.DisputedDate);
                _oldDisputedVerdicts.Add(newDisputedVerdict);
            }
        }

        private int CheckArticleState(Wiki wiki, string title) 
        {
            ParameterCollection parameters = new ParameterCollection();
            parameters.Add("titles", title);
            parameters.Add("redirects", "");            

            XmlDocument doc = wiki.Enumerate(parameters, true);
            XmlNode redirNode = doc.SelectSingleNode("//r");
            // redirection
            if (redirNode != null)
                return 1;
            XmlNode pageNode = doc.SelectSingleNode("//page");
            // error
            if (pageNode == null || pageNode.Attributes == null)
                return -1;
            // doesn't exist
            if (pageNode.Attributes["missing"] != null)
                return 2;
            // regular page
            return 0;
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

        private string SaveSection(Wiki wiki, string title, string section, string newtext, string comment)
        {
            string revId = "";
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    revId = wiki.SaveSection(title,
                                             section,
                                             newtext,
                                             comment);
                    return revId;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    System.Threading.Thread.Sleep(10000);
                    string cookieFile = string.Format("Cache{0}ru{0}cookie.jar", Path.DirectorySeparatorChar);
                    WikiCache.Login(wiki, Settings.Default.Login, Settings.Default.Password, cookieFile);
                }
            }

            return revId;
        }

        #region IModule Members

		public void Run (Wiki wiki)
		{
			UpdatePages (wiki);
			Analyze (wiki);
			UpdateMainPage (wiki);
			UpdateArchives (wiki);
		}
		
		#endregion
	}
}