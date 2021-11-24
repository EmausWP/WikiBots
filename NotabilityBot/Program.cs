using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using Claymore.SharpMediaWiki;
using Claymore.TalkCleanupWikiBot;
using NotabilityBot.Properties;

namespace Claymore.NotabilityBot
{
    class Program
    {
        static int Main(string[] args)
        {
            int days = 3;
            foreach (var arg in args)
            {
                if (arg.StartsWith("-days:"))
                {
                    if (!int.TryParse(arg.Substring(6), out days))
                        days = 3;
                }
            }

            if (string.IsNullOrEmpty(Settings.Default.Login) ||
                string.IsNullOrEmpty(Settings.Default.Password))
            {
                Console.Out.WriteLine("Please add login and password to the configuration file.");
                return -1;
            }

            Wiki wiki = new Wiki("https://ru.wikipedia.org/w/");
            wiki.SleepBetweenQueries = 2;
            Console.Out.WriteLine("Logging in as " + Settings.Default.Login + " to " + wiki.Uri + "...");
            try
            {
                Directory.CreateDirectory("Cache" + Path.DirectorySeparatorChar + "ru");
                string cookieFile = String.Format("Cache{0}ru{0}cookie.jar", Path.DirectorySeparatorChar);
                WikiCache.Login(wiki, Settings.Default.Login, Settings.Default.Password, cookieFile);

                if (!WikiCache.LoadNamespaces(wiki,
                                              String.Format("Cache{0}ru{0}namespaces.dat", Path.DirectorySeparatorChar)))
                {
                    wiki.GetNamespaces();
                    WikiCache.CacheNamespaces(wiki,
                                              String.Format(
                                                  "Cache{0}ru{0}namespaces.dat",
                                                  Path.DirectorySeparatorChar));
                }
            }
            catch (WikiException e)
            {
                Console.Out.WriteLine(e.Message);
                return -1;
            }
            Console.Out.WriteLine("Logged in as " + Settings.Default.Login + ".");

            string errorFileName = String.Format("Cache{0}ru{0}Errors.txt", Path.DirectorySeparatorChar);

            string criteriaPage = Settings.Default.Criteria;
            if (string.IsNullOrEmpty(criteriaPage))
                criteriaPage = @"User:AeroBot/Notability.css";
   //         var textString = wiki.LoadText(criteriaPage);
            var textString = wiki.LoadTextRev(criteriaPage);
            NotabilityCriteria criteria = new NotabilityCriteria();
            criteria.LoadCriteria(textString);

            string exclusionPage = Settings.Default.UserExclusions;
            if (string.IsNullOrEmpty(exclusionPage))
                exclusionPage = @"User:AeroBot/Notability/OptOut";
    //        textString = wiki.LoadText(exclusionPage);
            textString = wiki.LoadTextRev(exclusionPage);
            List<string> excludedUsers =
                textString.Split(
                    new[] { '\r', '\n', ',' },
                    StringSplitOptions.RemoveEmptyEntries).ToList();

            ArticlesForDeletionLocalization l10i = new ArticlesForDeletionLocalization();
            l10i.Category = "Категория:Википедия:Незакрытые обсуждения удаления страниц";
            l10i.Culture = "ru-RU";
            l10i.MainPage = "Википедия:К удалению";
            l10i.Template = "Удаление статей";
            l10i.TopTemplate = "/Заголовок";
            l10i.BottomTemplate = "/Подвал";
            l10i.Results = new string[] {"Итог", "Общий итог", "Автоматический итог", "Автоитог"};
            l10i.Language = "ru";
            l10i.MainPageUpdateComment = "обновление";
            l10i.ArchiveTemplate = "Статьи, вынесенные на удаление";
            l10i.ArchivePage = "Википедия:Архив запросов на удаление/";
            l10i.EmptyArchive = "нет обсуждений";
            l10i.Processor = null;
            l10i.StrikeOutComment = "зачёркивание заголовков";
            l10i.DateFormat = "d MMMM yyyy в HH:mm (UTC)";
            l10i.AutoResultComment = " и подведение итогов";
            l10i.AutoResultSection = "Автоитог";
            l10i.NotificationTemplate = "Оставлено";
            l10i.EmptyResult = "Пустой итог";
            l10i.ChallengedResult = "Оспоренный итог";
            l10i.ArchiveHeader = "{{Навигация по архиву КУ}}\n{{Удаление статей/Начало}}\n";
            l10i.ArchiveFooter = "{{Удаление статей/Конец}}";
       
            List<IModule> modules = new List<IModule>()
            {
                new NotabilityWikification(l10i, criteria, excludedUsers)
            };

            if (!File.Exists(errorFileName))
            {
                using (FileStream stream = File.Create(errorFileName)) { }
            }

            int lastIndex = 0;
            using (TextReader streamReader = new StreamReader(errorFileName))
            {
                string line = streamReader.ReadToEnd();
                if (!string.IsNullOrEmpty(line))
                {
                    lastIndex = int.Parse(line);
                }
            }

            for (int i = lastIndex; i < modules.Count; ++i)
            {
                try
                {
                    modules[i].Run(wiki, days);
                }
                catch (WikiException e)
                {
                    using (TextWriter streamWriter = new StreamWriter(errorFileName))
                    {
                        streamWriter.Write(i);
                    }
                    Console.WriteLine(e);
                    return -1;
                }
                catch (WebException)
                {
                    using (TextWriter streamWriter = new StreamWriter(errorFileName))
                    {
                        streamWriter.Write(i);
                    }
                    return -1;
                }
            }

            if (File.Exists(errorFileName))
            {
                File.Delete(errorFileName);
            }

            Console.Out.WriteLine("Done.");
            return 0;
        }
    }
    
    internal struct Day
    {
        public WikiPage Page;
        public DateTime Date;
        public bool Archived;
        public bool Exists;
    }
    /*
    internal struct DeleteLogEvent
    {
        public string Comment;
        public string User;
        public bool Deleted;
        public bool Restored;
        public DateTime Timestamp;
    }
    */
    internal struct ArticlesForDeletionLocalization
    {
        public string AutoResultSection;
        public string Language;
        public string Category;
        public string MainPage;
        public string Culture;
        public string Template;
        public string TopTemplate;
        public string BottomTemplate;
        public string[] Results;
        public string MainPageUpdateComment;
        public string ArchiveTemplate;
        public string ArchivePage;
        public string EmptyArchive;
        public NotabilityWikification.TitleProcessor Processor;
        public string StrikeOutComment;
        public string AutoResultMessage;
        public string DateFormat;
        public string AutoResultComment;
        public string NotificationTemplate;
        public string EmptyResult;
        public string ChallengedResult;
        public string ArchiveHeader;
        public string ArchiveFooter;
    }
    
    internal interface IModule
    {
        void Run(Wiki wiki, int days);
    }
}
