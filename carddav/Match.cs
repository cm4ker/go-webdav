using System;
using System.Collections.Generic;
using System.Linq;
using VCard;

namespace CardDav
{
    public static class Filter
    {
        public static List<AddressObject> Apply(AddressBookQuery query, List<AddressObject> aos)
        {
            if (query == null)
            {
                return aos;
            }

            var outList = new List<AddressObject>();
            foreach (var ao in aos)
            {
                bool ok;
                try
                {
                    ok = Match(query, ao);
                }
                catch (Exception)
                {
                    return null;
                }

                if (!ok)
                {
                    continue;
                }

                outList.Add(FilterProperties(query.DataRequest, ao));
            }
            return outList;
        }

        public static bool Match(AddressBookQuery query, AddressObject ao)
        {
            if (query == null)
            {
                return true;
            }

            switch (query.FilterTest)
            {
                default:
                    throw new InvalidOperationException($"Unknown query filter test {query.FilterTest}");

                case FilterTest.AnyOf:
                case FilterTest.None:
                    foreach (var prop in query.PropFilters)
                    {
                        bool ok;
                        try
                        {
                            ok = MatchPropFilter(prop, ao);
                        }
                        catch (Exception)
                        {
                            return false;
                        }

                        if (ok)
                        {
                            return true;
                        }
                    }
                    return false;

                case FilterTest.AllOf:
                    foreach (var prop in query.PropFilters)
                    {
                        bool ok;
                        try
                        {
                            ok = MatchPropFilter(prop, ao);
                        }
                        catch (Exception)
                        {
                            return false;
                        }

                        if (!ok)
                        {
                            return false;
                        }
                    }
                    return true;
            }
        }

        private static bool MatchPropFilter(PropFilter prop, AddressObject ao)
        {
            var field = ao.Card.Get(prop.Name);
            if (field == null)
            {
                return prop.IsNotDefined;
            }
            else if (prop.IsNotDefined)
            {
                return false;
            }

            if (prop.TextMatches.Count == 0)
            {
                return true;
            }

            switch (prop.Test)
            {
                default:
                    throw new InvalidOperationException($"Unknown property filter test {prop.Test}");

                case FilterTest.AnyOf:
                case FilterTest.None:
                    foreach (var txt in prop.TextMatches)
                    {
                        bool ok;
                        try
                        {
                            ok = MatchTextMatch(txt, field);
                        }
                        catch (Exception)
                        {
                            return false;
                        }

                        if (ok)
                        {
                            return true;
                        }
                    }
                    return false;

                case FilterTest.AllOf:
                    foreach (var txt in prop.TextMatches)
                    {
                        bool ok;
                        try
                        {
                            ok = MatchTextMatch(txt, field);
                        }
                        catch (Exception)
                        {
                            return false;
                        }

                        if (!ok)
                        {
                            return false;
                        }
                    }
                    return true;
            }
        }

        private static bool MatchTextMatch(TextMatch txt, VCardField field)
        {
            bool ok;
            switch (txt.MatchType)
            {
                default:
                    throw new InvalidOperationException($"Unknown textmatch type {txt.MatchType}");

                case MatchType.Equals:
                    ok = txt.Text == field.Value;
                    break;

                case MatchType.Contains:
                case MatchType.None:
                    ok = field.Value.Contains(txt.Text);
                    break;

                case MatchType.StartsWith:
                    ok = field.Value.StartsWith(txt.Text);
                    break;

                case MatchType.EndsWith:
                    ok = field.Value.EndsWith(txt.Text);
                    break;
            }

            if (txt.NegateCondition)
            {
                ok = !ok;
            }
            return ok;
        }

        private static AddressObject FilterProperties(AddressDataRequest req, AddressObject ao)
        {
            if (req.AllProp || req.Props.Count == 0)
            {
                return ao;
            }

            if (ao.Card.Count == 0)
            {
                throw new InvalidOperationException("Request to process empty vCard");
            }

            var result = new AddressObject
            {
                Path = ao.Path,
                ModTime = ao.ModTime,
                ETag = ao.ETag,
                Card = new VCardCard()
            };

            result.Card[VCardField.Version] = ao.Card[VCardField.Version];
            foreach (var prop in req.Props)
            {
                if (ao.Card.TryGetValue(prop, out var value))
                {
                    result.Card[prop] = value;
                }
            }

            return result;
        }
    }
}
