using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace TVRename
{
    /// <summary>
    /// Handles a thread-safe implementation of the 'library' this will hold all the ShowItem configuration as well
    /// many methods that provide summaries of the data in the library
    /// </summary>
    public class ShowLibrary : ConcurrentDictionary<int,ShowItem>
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        public IEnumerable<ShowItem> Shows => this.Values;

        private IEnumerable<string> GetSeasonWords()
        {
            //See https://github.com/TV-Rename/tvrename/issues/241 for background
            List<string> results = TVSettings.Instance.searchSeasonWordsArray.ToList();

            if (!TVSettings.Instance.ForceBulkAddToUseSettingsOnly)
            {
                IEnumerable<string> seasonWordsFromShows =
                    from si in this.Values select si.AutoAdd_SeasonFolderName.Trim();
                results = seasonWordsFromShows.Distinct().ToList();

                results.Add(TVSettings.Instance.defaultSeasonWord);
            }

            return results.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct();
        }

        private IEnumerable<string> SeasonWordsCache;

        internal IEnumerable<string> SeasonWords()
        {
            if (SeasonWordsCache == null) SeasonWordsCache = GetSeasonWords();
            return SeasonWordsCache;

        }

        public List<string> getGenres()
        {
            List<string> allGenres = new List<string> { };
            foreach (ShowItem si in this.Values)
            {
                if (si.Genres != null) allGenres.AddRange(si.Genres);
            }
            List<string> distinctGenres = allGenres.Distinct().ToList();
            distinctGenres.Sort();
            return distinctGenres;
        }

        public List<string> getStatuses()
        {
            List<string> allStatuses = new List<string> { };
            foreach (ShowItem si in this.Values)
            {
                if (si.ShowStatus != null) allStatuses.Add(si.ShowStatus);
            }
            List<string> distinctStatuses = allStatuses.Distinct().ToList();
            distinctStatuses.Sort();
            return distinctStatuses;
        }

        public List<string> getNetworks()
        {
            List<string> allValues = new List<string> { };
            foreach (ShowItem si in this.Values)
            {
                if (si.TheSeries()?.getNetwork() != null) allValues.Add(si.TheSeries().getNetwork());
            }
            List<string> distinctValues = allValues.Distinct().ToList();
            distinctValues.Sort();
            return distinctValues;
        }

        public List<string> GetContentRatings()
        {
            List<string> allValues = new List<string> { };
            foreach (ShowItem si in this.Values)
            {
                if (si.TheSeries()?.GetContentRating() != null) allValues.Add(si.TheSeries().GetContentRating());
            }
            List<string> distinctValues = allValues.Distinct().ToList();
            distinctValues.Sort();
            return distinctValues;
        }


        public int getMinYear() => this.Min(si => Convert.ToInt32(si.Value.TheSeries().GetYear()));

        public int getMaxYear() => this.Max(si => Convert.ToInt32(si.Value.TheSeries().GetYear()));



        public List<ShowItem> GetShowItems()
        {
            List<ShowItem> returnList = this.Values.ToList();
            returnList.Sort(new Comparison<ShowItem>(TVRename.ShowItem.CompareShowItemNames));
            return returnList;
        }


        public ShowItem ShowItem(int id)
        {
            return this.ContainsKey(id) ? this[id] : null;
        }

        public bool GenDict()
        {
            bool res = true;
            foreach (ShowItem si in this.Values)
            {
                if (!this.GenerateEpisodeDict(si))
                    res = false;
            }
            return res;
        }

        public bool GenerateEpisodeDict(ShowItem si)
        {
            si.SeasonEpisodes.Clear();

            // copy data from tvdb
            // process as per rules
            // done!

            TheTVDB.Instance.GetLock("GenerateEpisodeDict");

            SeriesInfo ser = TheTVDB.Instance.GetSeries(si.TVDBCode);

            if (ser == null)
            {
                TheTVDB.Instance.Unlock("GenerateEpisodeDict");
                return false; // TODO: warn user
            }

            bool r = true;
            Dictionary<int, Season> seasonsToUse = si.DVDOrder ?
                ser.DVDSeasons
                : ser.AiredSeasons;
            foreach (KeyValuePair<int, Season> kvp in seasonsToUse)
            {
                List<ProcessedEpisode> pel = GenerateEpisodes(si, ser, kvp.Key, true);
                si.SeasonEpisodes[kvp.Key] = pel;
                if (pel == null)
                    r = false;
            }

            List<int> theKeys = new List<int>();
            // now, go through and number them all sequentially
            foreach (int snum in seasonsToUse.Keys)
                theKeys.Add(snum);

            theKeys.Sort();

            int overallCount = 1;
            foreach (int snum in theKeys)
            {
                if (snum == 0) continue;

                foreach (ProcessedEpisode pe in si.SeasonEpisodes[snum])
                {
                    pe.OverallNumber = overallCount;
                    if (si.DVDOrder)
                    {
                        overallCount += 1 + pe.EpNum2 - pe.DVDEpNum;
                    }
                    else
                    {
                        overallCount += 1 + pe.EpNum2 - pe.AiredEpNum;
                    }
                }
            }

            TheTVDB.Instance.Unlock("GenerateEpisodeDict");

            return r;
        }

        public static List<ProcessedEpisode> GenerateEpisodes(ShowItem si, SeriesInfo ser, int snum, bool applyRules)
        {
            List<ProcessedEpisode> eis = new List<ProcessedEpisode>();

            if (ser == null) return null;

            Dictionary<int, Season> seasonsToUse = si.DVDOrder ? ser.DVDSeasons : ser.AiredSeasons;

            if (!seasonsToUse.ContainsKey(snum))
                return null; // todo.. something?

            Season seas = seasonsToUse[snum];

            if (seas == null)
                return null; // TODO: warn user

            foreach (Episode e in seas.Episodes)
                eis.Add(new ProcessedEpisode(e, si)); // add a copy

            if (si.DVDOrder)
            {
                eis.Sort(new System.Comparison<ProcessedEpisode>(ProcessedEpisode.DVDOrderSorter));
                Renumber(eis);
            }
            else
                eis.Sort(new System.Comparison<ProcessedEpisode>(ProcessedEpisode.EPNumberSorter));

            if (si.CountSpecials && seasonsToUse.ContainsKey(0))
            {
                // merge specials in
                foreach (Episode ep in seasonsToUse[0].Episodes)
                {
                    string seasstr = ep.AirsBeforeSeason;
                    string epstr = ep.AirsBeforeEpisode;
                    if ((string.IsNullOrEmpty(seasstr)) || (string.IsNullOrEmpty(epstr)))
                        continue;
                    int sease = int.Parse(seasstr);
                    if (sease != snum)
                        continue;
                    int epnum = int.Parse(epstr);
                    for (int i = 0; i < eis.Count; i++)
                    {
                        if ((eis[i].AppropriateSeasonNumber == sease) && (eis[i].AppropriateEpNum == epnum))
                        {
                            ProcessedEpisode pe = new ProcessedEpisode(ep, si)
                            {
                                TheAiredSeason = eis[i].TheAiredSeason,
                                TheDVDSeason = eis[i].TheDVDSeason,
                                SeasonID = eis[i].SeasonID
                            };
                            eis.Insert(i, pe);
                            break;
                        }
                    }
                }
                // renumber to allow for specials
                int epnumr = 1;
                foreach (ProcessedEpisode t in eis)
                {
                    t.EpNum2 = epnumr + (t.EpNum2 - t.AppropriateEpNum);
                    t.AppropriateEpNum = epnumr;
                    epnumr++;
                }
            }

            if (applyRules)
            {
                List<ShowRule> rules = si.RulesForSeason(snum);
                if (rules != null)
                    ApplyRules(eis, rules, si);
            }

            return eis;
        }

        internal void Add(ShowItem found)
        {
            if (!TryAdd(found.TVDBCode, found))
            {
                logger.Error($"Failed to Add {found.ShowName} with TVDBId={found.TVDBCode} to library");
            }
        }

        public static void ApplyRules(List<ProcessedEpisode> eis, List<ShowRule> rules, ShowItem si)
        {
            foreach (ShowRule sr in rules)
            {
                int nn1 = sr.First;
                int nn2 = sr.Second;

                int n1 = -1;
                int n2 = -1;
                // turn nn1 and nn2 from ep number into position in array
                for (int i = 0; i < eis.Count; i++)
                {
                    if (eis[i].AppropriateEpNum == nn1)
                    {
                        n1 = i;
                        break;
                    }
                }
                for (int i = 0; i < eis.Count; i++)
                {
                    if (eis[i].AppropriateEpNum == nn2)
                    {
                        n2 = i;
                        break;
                    }
                }

                if (sr.DoWhatNow == RuleAction.kInsert)
                {
                    // this only applies for inserting an episode, at the end of the list
                    if (nn1 == eis[eis.Count - 1].AppropriateEpNum + 1) // after the last episode
                        n1 = eis.Count;
                }

                string txt = sr.UserSuppliedText;
                int ec = eis.Count;

                switch (sr.DoWhatNow)
                {
                    case RuleAction.kRename:
                        {
                            if ((n1 < ec) && (n1 >= 0))
                                eis[n1].Name = txt;
                            break;
                        }
                    case RuleAction.kRemove:
                        {
                            if ((n1 < ec) && (n1 >= 0) && (n2 < ec) && (n2 >= 0))
                                eis.RemoveRange(n1, 1 + n2 - n1);
                            else if ((n1 < ec) && (n1 >= 0) && (n2 == -1))
                                eis.RemoveAt(n1);
                            break;
                        }
                    case RuleAction.kIgnoreEp:
                        {
                            if (n2 == -1)
                                n2 = n1;
                            for (int i = n1; i <= n2; i++)
                            {
                                if ((i < ec) && (i >= 0))
                                    eis[i].Ignore = true;
                            }
                            break;
                        }
                    case RuleAction.kSplit:
                        {
                            // split one episode into a multi-parter
                            if ((n1 < ec) && (n1 >= 0))
                            {
                                ProcessedEpisode ei = eis[n1];
                                string nameBase = ei.Name;
                                eis.RemoveAt(n1); // remove old one
                                for (int i = 0; i < nn2; i++) // make n2 new parts
                                {
                                    ProcessedEpisode pe2 = new ProcessedEpisode(ei, si, ProcessedEpisode.ProcessedEpisodeType.split)
                                    {
                                        Name = nameBase + " (Part " + (i + 1) + ")",
                                        AiredEpNum = -2,
                                        DVDEpNum = -2,
                                        EpNum2 = -2
                                    };
                                    eis.Insert(n1 + i, pe2);
                                }
                            }
                            break;
                        }
                    case RuleAction.kMerge:
                    case RuleAction.kCollapse:
                        {
                            if ((n1 != -1) && (n2 != -1) && (n1 < ec) && (n2 < ec) && (n1 < n2))
                            {
                                ProcessedEpisode oldFirstEI = eis[n1];
                                List<string> episodeNames = new List<string> { eis[n1].Name };
                                string defaultCombinedName = eis[n1].Name + " + ";
                                string combinedSummary = eis[n1].Overview + "<br/><br/>";
                                List<Episode> alleps = new List<Episode>();
                                alleps.Add(eis[n1]);
                                //int firstNum = eis[n1]->TVcomEpCount();
                                for (int i = n1 + 1; i <= n2; i++)
                                {
                                    episodeNames.Add(eis[i].Name);
                                    defaultCombinedName += eis[i].Name;
                                    combinedSummary += eis[i].Overview;
                                    alleps.Add(eis[i]);
                                    if (i != n2)
                                    {
                                        defaultCombinedName += " + ";
                                        combinedSummary += "<br/><br/>";
                                    }
                                }

                                eis.RemoveRange(n1, n2 - n1);

                                eis.RemoveAt(n1);

                                string combinedName = GetBestNameFor(episodeNames, defaultCombinedName);

                                ProcessedEpisode pe2 = new ProcessedEpisode(oldFirstEI, si, alleps)
                                {
                                    Name = ((string.IsNullOrEmpty(txt)) ? combinedName : txt),
                                    AiredEpNum = -2,
                                    DVDEpNum = -2
                                };
                                if (sr.DoWhatNow == RuleAction.kMerge)
                                    pe2.EpNum2 = -2 + n2 - n1;
                                else
                                    pe2.EpNum2 = -2;

                                pe2.Overview = combinedSummary;
                                eis.Insert(n1, pe2);
                            }
                            break;
                        }
                    case RuleAction.kSwap:
                        {
                            if ((n1 != -1) && (n2 != -1) && (n1 < ec) && (n2 < ec))
                            {
                                ProcessedEpisode t = eis[n1];
                                eis[n1] = eis[n2];
                                eis[n2] = t;
                            }
                            break;
                        }
                    case RuleAction.kInsert:
                        {
                            if ((n1 < ec) && (n1 >= 0))
                            {
                                ProcessedEpisode t = eis[n1];
                                ProcessedEpisode n = new ProcessedEpisode(t.TheSeries, t.TheAiredSeason, t.TheDVDSeason, si)
                                {
                                    Name = txt,
                                    AiredEpNum = -2,
                                    DVDEpNum = -2,
                                    EpNum2 = -2
                                };
                                eis.Insert(n1, n);
                            }
                            else if (n1 == eis.Count)
                            {
                                ProcessedEpisode t = eis[n1 - 1];
                                ProcessedEpisode n = new ProcessedEpisode(t.TheSeries, t.TheAiredSeason, t.TheDVDSeason, si)
                                {
                                    Name = txt,
                                    AiredEpNum = -2,
                                    DVDEpNum = -2,
                                    EpNum2 = -2
                                };
                                eis.Add(n);
                            }
                            break;
                        }
                } // switch DoWhatNow

                Renumber(eis);
            } // for each rule
            // now, go through and remove the ignored ones (but don't renumber!!)
            for (int i = eis.Count - 1; i >= 0; i--)
            {
                if (eis[i].Ignore)
                    eis.RemoveAt(i);
            }
        }

        public static string GetBestNameFor(List<string> episodeNames, string defaultName)
        {
            string root = Helpers.GetCommonStartString(episodeNames);
            int shortestEpisodeName = episodeNames.Min(x => x.Length);
            int longestEpisodeName = episodeNames.Max(x => x.Length);
            bool namesSameLength = (shortestEpisodeName == longestEpisodeName);
            bool rootIsIgnored = root.Trim().Equals("Episode", StringComparison.OrdinalIgnoreCase) ||
                                 root.Trim().Equals("Part", StringComparison.OrdinalIgnoreCase);

            if (!namesSameLength || rootIsIgnored || root.Length <= 3 || root.Length <= shortestEpisodeName / 2)
                return defaultName;


            char[] charsToTrim = { ',', '.', ';', ':', '-', '(' };
            string[] wordsToTrim = { "part", "episode" };

            return root.Trim().TrimEnd(wordsToTrim).Trim().TrimEnd(charsToTrim).Trim();
        }

        public static void Renumber(List<ProcessedEpisode> eis)
        {
            if (eis.Count == 0)
                return; // nothing to do

            // renumber 
            // pay attention to specials etc.
            int n = (eis[0].AppropriateEpNum == 0) ? 0 : 1;

            foreach (ProcessedEpisode t in eis)
            {
                if (t.AppropriateEpNum == -1) continue;

                int num = t.EpNum2 - t.AppropriateEpNum;
                t.AppropriateEpNum = n;
                t.EpNum2 = n + num;
                n += num + 1;
            }
        }

        internal List<ShowItem> getRecentShows()
        {
            // only scan "recent" shows
            List<ShowItem> shows = new List<ShowItem>();
            int dd = TVSettings.Instance.WTWRecentDays;

            // for each show, see if any episodes were aired in "recent" days...
            foreach (ShowItem si in this.GetShowItems())
            {
                bool added = false;

                foreach (KeyValuePair<int, List<ProcessedEpisode>> kvp in si.SeasonEpisodes)
                {
                    if (added)
                        break;

                    if (si.IgnoreSeasons.Contains(kvp.Key))
                        continue; // ignore this season

                    List<ProcessedEpisode> eis = kvp.Value;

                    foreach (ProcessedEpisode ei in eis)
                    {
                        DateTime? dt = ei.GetAirDateDT(true);
                        if ((dt != null) && (dt.Value.CompareTo(DateTime.MaxValue) != 0))
                        {
                            TimeSpan ts = dt.Value.Subtract(DateTime.Now);
                            if ((ts.TotalHours >= (-24 * dd)) && (ts.TotalHours <= 0)) // fairly recent?
                            {
                                shows.Add(si);
                                added = true;
                                break;
                            }
                        }
                    }
                }
            }
            return shows;
        }

        public List<ProcessedEpisode> NextNShows(int nShows, int nDaysPast, int nDaysFuture)
        {
            DateTime notBefore = DateTime.Now.AddDays(-nDaysPast);
            List<ProcessedEpisode> found = new List<ProcessedEpisode>();

            for (int i = 0; i < nShows; i++)
            {
                ProcessedEpisode nextAfterThat = null;
                TimeSpan howClose = TimeSpan.MaxValue;
                foreach (ShowItem si in this.GetShowItems())
                {
                    if (!si.ShowNextAirdate)
                        continue;
                    foreach (KeyValuePair<int, List<ProcessedEpisode>> v in si.SeasonEpisodes)
                    {
                        if (si.IgnoreSeasons.Contains(v.Key))
                            continue; // ignore this season

                        foreach (ProcessedEpisode ei in v.Value)
                        {
                            if (found.Contains(ei))
                                continue;

                            DateTime? airdt = ei.GetAirDateDT(true);

                            if ((airdt == null) || (airdt == DateTime.MaxValue))
                                continue;
                            DateTime dt = (DateTime)airdt;

                            TimeSpan ts = dt.Subtract(notBefore);
                            TimeSpan timeUntil = dt.Subtract(DateTime.Now);
                            if (((howClose == TimeSpan.MaxValue) || (ts.CompareTo(howClose) <= 0) && (ts.TotalHours >= 0)) && (ts.TotalHours >= 0) && (timeUntil.TotalDays <= nDaysFuture))
                            {
                                howClose = ts;
                                nextAfterThat = ei;
                            }
                        }
                    }
                }
                if (nextAfterThat == null)
                    return found;

                DateTime? nextdt = nextAfterThat.GetAirDateDT(true);
                if (nextdt.HasValue)
                {
                    notBefore = nextdt.Value;
                    found.Add(nextAfterThat);
                }
            }

            return found;
        }

        public void AddRange(IEnumerable<ShowItem> addedShows)
        {
            foreach (ShowItem show in addedShows)
            {
                Add(show);
            }
        }

        internal void Remove(ShowItem si)
        {
            if (!TryRemove(si.TVDBCode,out _))
            {
                logger.Error($"Failed to remove {si.ShowName} from the library with TVDBId={si.TVDBCode}");
            }
        }

        public static bool HasAnyAirdates(ShowItem si, int snum)
        {
            SeriesInfo ser = TheTVDB.Instance.GetSeries(si.TVDBCode);

            if (ser == null) return false;

            Dictionary<int, Season> seasonsToUse = si.DVDOrder ? ser.DVDSeasons : ser.AiredSeasons;

            if (!seasonsToUse.ContainsKey(snum)) return false;

            foreach (Episode e in seasonsToUse[snum].Episodes)
            {
                if (e.FirstAired != null) return true;

            }
            return false;
        }

        public IEnumerable<ProcessedEpisode> GetRecentAndFutureEps(int days)
        {
            List<ProcessedEpisode> returnList = new List<ProcessedEpisode> { };

            foreach (ShowItem si in this.Values)
            {
                if (!si.ShowNextAirdate)
                    continue;

                foreach (KeyValuePair<int, List<ProcessedEpisode>> kvp in si.SeasonEpisodes)
                {
                    if (si.IgnoreSeasons.Contains(kvp.Key))
                        continue; // ignore this season

                    List<ProcessedEpisode> eis = kvp.Value;

                    bool nextToAirFound = false;

                    foreach (ProcessedEpisode ei in eis)
                    {
                        DateTime? dt = ei.GetAirDateDT(true);
                        if ((dt != null) && (dt.Value.CompareTo(DateTime.MaxValue) != 0))
                        {
                            TimeSpan ts = dt.Value.Subtract(DateTime.Now);
                            if (ts.TotalHours >= (-24 * days)) // in the future (or fairly recent)
                            {
                                if ((ts.TotalHours >= 0) && (!nextToAirFound))
                                {
                                    nextToAirFound = true;
                                    ei.NextToAir = true;
                                }
                                else
                                    ei.NextToAir = false;
                                returnList.Add(ei);
                            }
                        }
                    }
                }

            }
            return returnList;
        }



        public void LoadFromXML(XmlReader r2)
        {
            r2.Read();
            r2.Read();
            while (!r2.EOF)
            {
                if ((r2.Name == "MyShows") && (!r2.IsStartElement()))
                    break;
                if (r2.Name == "ShowItem")
                {
                    ShowItem si = new ShowItem(r2.ReadSubtree());

                    if (si.UseCustomShowName) // see if custom show name is actually the real show name
                    {
                        SeriesInfo ser = si.TheSeries();
                        if ((ser != null) && (si.CustomShowName == ser.Name))
                        {
                            // then, turn it off
                            si.CustomShowName = "";
                            si.UseCustomShowName = false;
                        }
                    }

                    this.Add(si);

                    r2.Read();
                }
                else
                    r2.ReadOuterXml();
            }

        }
    }
}
