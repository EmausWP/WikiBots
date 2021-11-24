using System;

namespace Claymore.TalkCleanupWikiBot
{
	public class RuDateTime
	{
		public static string MonthCast (string sDate)
		{
			string enDate = sDate.ToLower ().Replace ("января", "january").Replace ("январь", "january").Replace ("февраля", "february").Replace ("февраль", "february");
			enDate = enDate.Replace ("марта", "march").Replace ("март", "march").Replace ("апреля", "april").Replace ("апрель", "april");
			enDate = enDate.Replace ("мая", "may").Replace ("май", "may").Replace ("июня", "june").Replace ("июнь", "june");
			enDate = enDate.Replace ("июля", "july").Replace ("июль", "july").Replace ("августа", "august").Replace ("август", "august");
			enDate = enDate.Replace ("сентября", "september").Replace ("сентябрь", "september").Replace ("октября", "october").Replace ("октябрь", "october");
			enDate = enDate.Replace ("ноября", "november").Replace ("ноябрь", "november").Replace ("декабря", "december").Replace ("декабрь", "december");
			
			return enDate;
		}

		public static string MonthNormalize (string sDate)
		{
			string nDate = sDate.ToLower ().Replace ("января", "январь").Replace ("февраля", "февраль");
			nDate = nDate.Replace ("марта", "март").Replace ("апреля", "апрель");
			nDate = nDate.Replace ("мая", "май").Replace ("июня", "июнь");
			nDate = nDate.Replace ("июля", "июль").Replace ("августа", "август");
			nDate = nDate.Replace ("сентября", "сентябрь").Replace ("октября", "октябрь");
			nDate = nDate.Replace ("ноября", "ноябрь").Replace ("декабря", "декабрь");
			
			return nDate;
		}
	}
}
