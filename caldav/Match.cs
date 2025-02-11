using System;
using System.Collections.Generic;
using System.Linq;

namespace CalDav
{
    public static class Filter
    {
        public static List<CalendarObject> Apply(CalendarQuery query, List<CalendarObject> cos)
        {
            if (query == null)
            {
                return cos;
            }

            var outList = new List<CalendarObject>();
            foreach (var co in cos)
            {
                bool ok;
                try
                {
                    ok = Match(query.CompFilter, co);
                }
                catch (Exception)
                {
                    return null;
                }

                if (!ok)
                {
                    continue;
                }

                outList.Add(co);
            }
            return outList;
        }

        public static bool Match(CompFilter query, CalendarObject co)
        {
            if (co.Data == null || co.Data.Component == null)
            {
                throw new InvalidOperationException("request to process empty calendar object");
            }
            return Match(query, co.Data.Component);
        }

        private static bool Match(CompFilter filter, CalendarComponent comp)
        {
            if (comp.Name != filter.Name)
            {
                return filter.IsNotDefined;
            }

            if (filter.Start != DateTime.MinValue)
            {
                bool match;
                try
                {
                    match = MatchCompTimeRange(filter.Start, filter.End, comp);
                }
                catch (Exception)
                {
                    return false;
                }

                if (!match)
                {
                    return false;
                }
            }

            foreach (var compFilter in filter.Comps)
            {
                bool match;
                try
                {
                    match = MatchCompFilter(compFilter, comp);
                }
                catch (Exception)
                {
                    return false;
                }

                if (!match)
                {
                    return false;
                }
            }

            foreach (var propFilter in filter.Props)
            {
                bool match;
                try
                {
                    match = MatchPropFilter(propFilter, comp);
                }
                catch (Exception)
                {
                    return false;
                }

                if (!match)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MatchCompFilter(CompFilter filter, CalendarComponent comp)
        {
            var matches = new List<CalendarComponent>();

            foreach (var child in comp.Children)
            {
                bool match;
                try
                {
                    match = Match(filter, child);
                }
                catch (Exception)
                {
                    return false;
                }

                if (match)
                {
                    matches.Add(child);
                }
            }

            if (matches.Count == 0)
            {
                return filter.IsNotDefined;
            }

            return true;
        }

        private static bool MatchPropFilter(PropFilter filter, CalendarComponent comp)
        {
            var field = comp.Props.Get(filter.Name);
            if (field == null)
            {
                return filter.IsNotDefined;
            }

            foreach (var paramFilter in filter.ParamFilter)
            {
                if (!MatchParamFilter(paramFilter, field))
                {
                    return false;
                }
            }

            if (filter.Start != DateTime.MinValue)
            {
                bool match;
                try
                {
                    match = MatchPropTimeRange(filter.Start, filter.End, field);
                }
                catch (Exception)
                {
                    return false;
                }

                if (!match)
                {
                    return false;
                }
            }
            else if (filter.TextMatch != null)
            {
                if (!MatchTextMatch(filter.TextMatch, field.Value))
                {
                    return false;
                }
                return true;
            }

            return true;
        }

        private static bool MatchCompTimeRange(DateTime start, DateTime end, CalendarComponent comp)
        {
            var rset = comp.RecurrenceSet(start.Kind);
            if (rset != null)
            {
                return rset.Between(start, end, true).Count > 0;
            }

            if (comp.Name != "VEVENT")
            {
                return false;
            }

            var eventStart = comp.DateTimeStart(start.Kind);
            var eventEnd = comp.DateTimeEnd(end.Kind);

            if (eventStart > start && (end == DateTime.MinValue || eventStart < end))
            {
                return true;
            }

            if (eventEnd > start && (end == DateTime.MinValue || eventEnd < end))
            {
                return true;
            }

            if (eventStart < start && (end != DateTime.MinValue && eventEnd > end))
            {
                return true;
            }

            return false;
        }

        private static bool MatchPropTimeRange(DateTime start, DateTime end, CalendarProp field)
        {
            var ptime = field.DateTime(start.Kind);
            if (ptime > start && (end == DateTime.MinValue || ptime < end))
            {
                return true;
            }
            return false;
        }

        private static bool MatchParamFilter(ParamFilter filter, CalendarProp field)
        {
            var value = field.Params.Get(filter.Name);
            if (value == null)
            {
                return filter.IsNotDefined;
            }
            else if (filter.IsNotDefined)
            {
                return false;
            }

            if (filter.TextMatch != null)
            {
                return MatchTextMatch(filter.TextMatch, value);
            }

            return true;
        }

        private static bool MatchTextMatch(TextMatch txt, string value)
        {
            var match = value.Contains(txt.Text);
            if (txt.NegateCondition)
            {
                match = !match;
            }
            return match;
        }
    }
}
