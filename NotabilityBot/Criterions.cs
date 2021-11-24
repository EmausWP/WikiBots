using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Claymore.TalkCleanupWikiBot
{
    class Criterion
    {
        private List<String> _links = new List<string>();
        private List<String> _categories = new List<string>();
        public void AddLinks(String[] items)
        {
            foreach (var normalizedItem in items.Select(item => NormalizeTitle(item)).Where(item => item.Length > 0))
            {
                _links.Add(normalizedItem);
            }
        }

        public void AddCategories(String[] items)
        {
            foreach (var normalizedItem in items.Select(item => NormalizeTitle(item)).Where(item => item.Length > 0))
            {
                _categories.Add(normalizedItem);
            }
        }

        private string NormalizeTitle(String src)
        {
            src = src.Trim();
            if (src.Length > 1)
                src = src[0].ToString().ToUpper() + src.Substring(1);

            return src;
        }

        public bool IsThisNotabilityLink(string link)
        {
            return _links.Any(lnk => NormalizeTitle(link) == lnk);
        }

        public string CheckCategories(IEnumerable<String> categories)
        {
        //    if (_categories.Count == 0 && _links.Count != 0)
        //        return _links[0];
            return categories.Any(pageCat => _categories.Contains(NormalizeTitle(pageCat))) ? _links[0] : "";
        }

        public string GetDefaultLink()
        {
            return _links.Count == 0 ? "" : _links[0];
        }
    }

    class NotabilityCriteria
    {
        private List<Criterion> _criteria;

        public void LoadCriteria(String text)
        {
            _criteria = new List<Criterion>();
            var newCriterion = new Criterion();

            var strs = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var str in strs)
            {
                var sstr = str.Trim();
                if (sstr.StartsWith("*"))
                {
                    newCriterion = new Criterion();
                    _criteria.Add(newCriterion);
                    var items = sstr.Substring(1).Split(';');
                    newCriterion.AddLinks(items);
                }
                if (sstr.StartsWith("#"))
                {
                    var items = sstr.Substring(1).Split(';');
                    newCriterion.AddCategories(items);
                }
            }
        }

        public bool IsThisNotabilityLink(string link)
        {
            return _criteria.Any(crt => crt.IsThisNotabilityLink(link));
        }

        public String GetDefaultLink()
        {
            return _criteria.Count == 0 ? "" : _criteria[_criteria.Count - 1].GetDefaultLink();
        }

        public String FindNoRelatedCriterion(IEnumerable<String> categories)
        {
            foreach (var res in _criteria.Select(crt => crt.CheckCategories(categories)).Where(res => res.Length > 0))
            {
                return res;
            }
            return GetDefaultLink();
        }
        
    }
}
