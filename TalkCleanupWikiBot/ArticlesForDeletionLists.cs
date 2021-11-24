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
    internal class ArticlesForDeletion3 : IModule
    {
        private ArticlesForDeletionLocalization _l10i;
        private string _cacheDir;
        private List<DisputedVerdict> _disputedVerdicts;
        private List<DisputedVerdict> _priorVerdicts;
        private List<DisputedVerdict> _newVerdicts;
        private List<string> _administrators;
		private List<string> _closers;


        public ArticlesForDeletion3(ArticlesForDeletionLocalization l10i)
        {
            _l10i = l10i;
			_cacheDir = "Cache" + Path.DirectorySeparatorChar + _l10i.Language + 
				Path.DirectorySeparatorChar + "ArticlesForDeletion" + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(_cacheDir);
        }

        public void UpdatePages(Wiki wiki)
        {
            Console.Out.WriteLine("Updating articles for deletion...");
            Regex wikiLinkRE = new Regex(@"\[{2}(.+?)(\|.+?)?]{2}");
            Regex timeRE = new Regex(@"(\d{2}:\d{2}\, \d\d? [а-я]+ \d{4}) \(UTC\)");
			_disputedVerdicts = new List<DisputedVerdict> ();
			_priorVerdicts = new List<DisputedVerdict> ();
            _newVerdicts = new List<DisputedVerdict>();

            _administrators = new List<string> ();
			_closers = new List<string> ();
			GetUserGroup (wiki, "sysop", _administrators);
			GetUserGroup (wiki, "closer", _closers);

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
			foreach (XmlNode page in pages) {
				string starttimestamp = queryTimestamp;
				int results = 0;
				string prefix = _l10i.MainPage + "/";
				string pageName = page.Attributes ["title"].Value;

				string basetimestamp = page.FirstChild.FirstChild.Attributes ["timestamp"].Value;
				string editToken = page.Attributes ["edittoken"].Value;

				if (pageName.Length < prefix.Length) {
					continue;
				}
				string date = pageName.Substring (prefix.Length);
				Day day = new Day ();
				if (!DateTime.TryParse (date, CultureInfo.CreateSpecificCulture (_l10i.Culture),
					                DateTimeStyles.AssumeUniversal, out day.Date)) {
					continue;
				}

				string text = "";
				string fileName = _cacheDir + date + ".bin";
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
					try {
						Console.Out.WriteLine ("Downloading " + pageName + "...");
                        text = wiki.LoadTextRev (pageName);
						starttimestamp = DateTime.Now.ToUniversalTime ().ToString ("yyyy-MM-ddTHH:mm:ssZ");
					} catch (WikiPageNotFound) {
						continue;
					}
					using (FileStream fs = new FileStream (fileName, FileMode.Create))
					using (GZipStream gs = new GZipStream (fs, CompressionMode.Compress))
					using (StreamWriter sw = new StreamWriter (gs)) {
						sw.WriteLine (page.Attributes ["lastrevid"].Value);
						sw.Write (text);
					}
				}


				List<string> titlesWithResults = new List<string> ();
				Dictionary<string, List<WikiPageSection>> titles = new Dictionary<string, List<WikiPageSection>> ();
				day.Page = WikiPage.Parse (pageName, text);
				foreach (WikiPageSection section in day.Page.Sections) {
					var hasResult = section.Subsections.Any(s => _l10i.Results.Any(r => r == s.Title.Trim())
															|| _l10i.AutoResultSection == s.Title.Trim());
                    if (hasResult)
                    {
                        var newVerdict = GetVerdict(day, section, 2);
                        if (newVerdict != null && DateTime.UtcNow.Subtract(newVerdict.VerdictDate).TotalDays <= 14)
                            _newVerdicts.Add(newVerdict);
                        continue;
                    }
					var hasDisputedResult = section.Subsections.Any(s => _l10i.DisputedResults.Any(r => r.ToLower() == s.Title.ToLower().Trim()));
					var hasPriorResult = section.Subsections.Any (s => _l10i.PriorResults.Any (r => r.ToLower() == s.Title.ToLower().Trim ()))				                     
					                     && !hasDisputedResult;
					
					if (hasDisputedResult) {						
						var newVerdict = GetVerdict (day, section, 0);
						if (newVerdict != null)
							_disputedVerdicts.Add (newVerdict);
					}
					if (hasPriorResult) {
						var newVerdict = GetVerdict (day, section, 1);
						if (newVerdict != null)
							_priorVerdicts.Add (newVerdict);
					}

					List<WikiPageSection> sections = new List<WikiPageSection> ();
					section.Reduce (sections, SubsectionsList);
					foreach (WikiPageSection subsection in sections) {							
						Match m = wikiLinkRE.Match (subsection.Title);                            
						if (!m.Success)
							continue;
						var hasSubResult = subsection.Subsections.Count (s => _l10i.Results.Any (r => r == s.Title.Trim ())
						                   || _l10i.AutoResultSection == s.Title.Trim ()) > 0;
						var hasDisputedSubResult = subsection.Subsections.Any(s => _l10i.DisputedResults.Any(r => r.ToLower() == s.Title.ToLower().Trim()))
															 && !hasSubResult;
						var hasPriorSubResult = subsection.Subsections.Any(s => _l10i.PriorResults.Any(r => r.ToLower() == s.Title.ToLower().Trim()))
						                                  && !hasSubResult
														  && !hasDisputedSubResult;    

						if (hasDisputedSubResult) {							
							var newVerdict = GetVerdict (day, subsection, 0);
							if (newVerdict != null)
								_disputedVerdicts.Add (newVerdict);
						}
						if (hasPriorSubResult) {
							var newVerdict = GetVerdict (day, subsection, 1);
							if (newVerdict != null)
								_priorVerdicts.Add (newVerdict);
						}
                        if (hasSubResult)
                        {
                            var newVerdict = GetVerdict(day, subsection, 2);
                            if (newVerdict != null && DateTime.UtcNow.Subtract(newVerdict.VerdictDate).TotalDays <= 14)
                                _newVerdicts.Add(newVerdict);
                        }

                    }
				}
			}

			_disputedVerdicts = _disputedVerdicts.OrderByDescending(v => v.NominationDate).ThenBy(v => v.VerdictDate).ToList();
			using (StreamWriter sw = new StreamWriter(_cacheDir + "Disputed.txt"))
			{
				sw.WriteLine("{{/Шапка}}\n");
				sw.WriteLine("{| class='wikitable sortable' style='text-align:left;'\n! Номинация \n! Обсуждение\n! Подведён<br/>итог\n! Автор итога");
				foreach (var v in _disputedVerdicts)
				{
					sw.WriteLine("|-\n| {0} || {1} || {2:yyyy.MM.dd} || {3}",
						v.Title,
						v.NominationTitle,
						v.VerdictDate,
						v.FullAuthorString
					);
				}
				sw.WriteLine("|}\n[[Категория:Википедия:Удаление страниц]]");
			}

			_priorVerdicts = _priorVerdicts.OrderByDescending(v => v.VerdictDate).ThenBy(v => v.DisputedDate).ToList();
			using (StreamWriter sw = new StreamWriter(_cacheDir + "Prior.txt"))
			{
				sw.WriteLine("{{/Шапка}}\n");
				sw.WriteLine("{| class='wikitable sortable' style='text-align:left;'\n! Номинация \n! Обсуждение\n! Подведён предварительный<br/>итог\n! Автор итога");
				foreach (var v in _priorVerdicts)
				{
					sw.WriteLine("|-\n| {0} || {1} || {2:yyyy.MM.dd} || {3}",
						v.Title,
						v.NominationTitle,
						v.VerdictDate, 
						v.FullAuthorString
					);
				}

				sw.WriteLine("|}\n[[Категория:Википедия:Удаление страниц]]");
			}

            _newVerdicts = _newVerdicts.OrderByDescending(v => v.VerdictDate).ThenBy(v => v.NominationDate).ToList();
            using (StreamWriter sw = new StreamWriter(_cacheDir + "New.txt"))
            {
                sw.WriteLine("{{/Шапка}}\n");
                sw.WriteLine("{| class='wikitable sortable' style='text-align:left;'\n! Номинация \n! Обсуждение\n! Подведён итог\n! Автор итога");
                foreach (var v in _newVerdicts)
                {
                    sw.WriteLine("|-\n| {0} || {1} || {2:yyyy.MM.dd} || {3}",
                        v.Title,
                        v.NominationTitle,
                        v.VerdictDate,
                        v.FullAuthorString
                    );
                }

                sw.WriteLine("|}\n[[Категория:Википедия:Удаление страниц]]");
            }

            Console.Out.WriteLine("Updating disputed verdicts...");
			using (TextReader sr = new StreamReader(_cacheDir + "Disputed.txt"))
			{
				string text = sr.ReadToEnd();
				Save(wiki, "Википедия:К удалению/Оспоренные итоги", text, "обновление");
			}
			Console.Out.WriteLine("Updating prior verdicts...");
			using (TextReader sr = new StreamReader(_cacheDir + "Prior.txt"))
			{
				string text = sr.ReadToEnd();
				Save(wiki, "Википедия:К удалению/Предварительные итоги", text, "обновление");	                  
			}
            Console.Out.WriteLine("Updating prior verdicts...");
            using (TextReader sr = new StreamReader(_cacheDir + "New.txt"))
            {
                string text = sr.ReadToEnd();
                Save(wiki, "Википедия:К удалению/Новые итоги", text, "обновление");
            }
        }

     
        internal static int CompareDays(Day x, Day y)
        {
            return y.Date.CompareTo(x.Date);
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

  /*      static int CompareDeleteLogEvents(DeleteLogEvent x, DeleteLogEvent y)
        {
            return y.Timestamp.CompareTo(x.Timestamp);
        }
*/
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

		DisputedVerdict GetVerdict(Day day, WikiPageSection section, int verdictType)
		{			
			Regex timeRE = new Regex(@"(\d{2}:\d{2}\, \d\d? [а-я]+ \d{4}) \(UTC\)");
			Regex userRE = new Regex(@"\[\[\s*(User:|U:|Участник:|Участница:|У:|User talk:|UT:|Обсуждение участника:|Обсуждение участницы:|ОУ:)\s*([^\]\|#]+)\s*[\]\|#]", RegexOptions.IgnoreCase);
			Regex anonRE = new Regex(@"\[\[\Special:Contributions/([^\]\|#]+)\s*[\]\|#]");
			DisputedVerdict newVerdict = new DisputedVerdict ();
			WikiPageSection verdictSection;
			if (verdictType == 0) 
				verdictSection = 
                    section.Subsections.Where (s => _l10i.DisputedResults.Any (r => r.ToLower() == s.Title.ToLower().Trim ())).FirstOrDefault ();			
			else if (verdictType == 1)
                verdictSection = 
                    section.Subsections.Where (s => _l10i.PriorResults.Any (r => r.ToLower() == s.Title.ToLower().Trim ())).FirstOrDefault ();			
            else
                verdictSection = 
                    section.Subsections.Where(s => _l10i.Results.Any(r => r == s.Title.Trim())
                                                            || _l10i.AutoResultSection == s.Title.Trim()).FirstOrDefault();

            if (verdictSection == null)
				return newVerdict;

			var timeMatch = timeRE.Match(verdictSection.Text);
			DateTime verdictDate;
			if (timeMatch.Success)
			{
				DateTime.TryParse(timeMatch.Groups[1].Value.Trim(), 
					CultureInfo.CreateSpecificCulture("ru-RU"),
					DateTimeStyles.AssumeUniversal,
					out verdictDate);				
			}
			else
			{
				verdictDate = new DateTime ();
			}
			int userPos = -1;
			string verdictAuthor = "";
			string fullAuthorString = "";
			int userType = -1;
			var userMatches = userRE.Matches (verdictSection.Text);
			foreach(Match m in userMatches)
			{
				if (m.Index > timeMatch.Index)
					continue;
				if (m.Index > userPos) 
				{
					userPos = m.Index;
					verdictAuthor = m.Groups [2].Value.Trim ();
					userType = 1;
					fullAuthorString = string.Format ("[[Участник:{0}|{0}]]", verdictAuthor);
					if(_administrators.Contains(verdictAuthor))
						fullAuthorString += " (А)";
					else if(_closers.Contains(verdictAuthor))
						fullAuthorString += " (ПИ)";
				}
			}
			var anonMatches = anonRE.Matches (verdictSection.Text);
			foreach(Match m in anonMatches)
			{
				if (m.Index > timeMatch.Index)
					continue;
				if (m.Index > userPos) 
				{
					userPos = m.Index;
					verdictAuthor = m.Groups [1].Value.Trim ();
					userType = 0;
					fullAuthorString = string.Format ("[[Special:Contributions/{0}|{0}]]", verdictAuthor);
				}
			}

			int ind = day.Page.Title.IndexOf("/");
			string dayName = day.Page.Title.Substring(ind + 1).Trim();
			newVerdict.Title = section.Title.Replace("<s>", "").Replace("</s>", "").Trim();
            var sectionLink = section.Title.Replace("<s>", "").Replace("</s>", "").Trim(new[] { ' ', '[', ']', ':', '|' }).Replace("[", "").Replace("]", "");
            if (sectionLink.Contains("|"))
                sectionLink = sectionLink.Substring(sectionLink.IndexOf("|") + 1).Trim();
            newVerdict.NominationTitle =
				string.Format ("[[{0}#{1}|{2}]]", 
					day.Page.Title,
                    sectionLink,
                    dayName);
			newVerdict.NominationDate = day.Date;
			newVerdict.VerdictDate = verdictDate;
			newVerdict.VerdictAuthor = verdictAuthor;
			newVerdict.FullAuthorString = fullAuthorString;

			return newVerdict;
		}

		private void GetUserGroup(Wiki wiki, string userGroupName, List<string> userGroup)
		{
			userGroup.Clear ();
			ParameterCollection parameters = new ParameterCollection();
			parameters.Add("list", "allusers");			 
			parameters.Add("augroup", userGroupName);
			parameters.Add("aulimit", "max");
			XmlDocument doc = wiki.Enumerate(parameters, true);

			XmlNodeList users = doc.SelectNodes("//u");
			foreach (XmlNode user in users) 
			{
				string userName = user.Attributes ["name"].Value.Trim();
				if (userName.Length > 0)
					userGroup.Add (userName);
			}
			Console.WriteLine ("Total found users in group \"{0}\": {1}", userGroupName, userGroup.Count);
		}

		private Tuple<int, int, int> TryGetVersionDiff(Wiki wiki, string pageName, DateTime day)
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
			return new Tuple<int, int, int>(0, 0, 0);
        }

		private Tuple<int, int, int> GetVersionDiff(Wiki wiki, string pageName, DateTime day)
        {
			if (!pageName.Contains("[[") || pageName.Contains("<s>"))
				return new Tuple<int, int, int> ( 0, 0, 0 );
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
				return new Tuple<int, int, int>(0, 0, 0);
            int startSize = revisions.OrderBy(r => r.Key).First().Value;
            int endSize = revisions.OrderByDescending(r => r.Key).First().Value;            

            int diffSize = endSize - startSize;
			var revNum = revisions.Count - 1;
			Console.WriteLine("{0}:\t{1} -> {2}", title, startSize, endSize);
			return new Tuple<int, int, int> (startSize, endSize, revNum);
        }


        #region IModule Members

        public void Run(Wiki wiki)
        {
            UpdatePages(wiki);
     //       Analyse(wiki);

        }

        #endregion
    }
}
