// 
// Main website for TVRename is http://tvrename.com
// 
// Source code available at http://code.google.com/p/tvrename/
// 
// This code is released under GPLv3 http://www.gnu.org/licenses/gpl.html
// 
using System;
using Alphaleonis.Win32.Filesystem;
using System.Xml;
using System.Collections.Generic;
using System.Linq;

// These are what is used when processing folders for missing episodes, renaming, etc. of files.

// A "ProcessedEpisode" is generated by processing an Episode from thetvdb, and merging/renaming/etc.
//
// A "ShowItem" is a show the user has added on the "My Shows" tab

// TODO: C++ to C# conversion stopped it using some of the typedefs, such as "IgnoreSeasonList".  (a) probably should
// rename that to something more generic like IntegerList, and (b) then put it back into the classes & functions
// that use it (e.g. ShowItem.IgnoreSeasons)

namespace TVRename
{
    public class ProcessedEpisode : Episode
    {
        public int EpNum2; // if we are a concatenation of episodes, this is the last one in the series. Otherwise, same as EpNum
        public bool Ignore;
        public bool NextToAir;
        public int OverallNumber;
        public ShowItem SI;

        public ProcessedEpisode(SeriesInfo ser, Season seas, ShowItem si)
            : base(ser, seas)
        {
            this.NextToAir = false;
            this.OverallNumber = -1;
            this.Ignore = false;
            this.EpNum2 = this.EpNum;
            this.SI = si;
        }

        public ProcessedEpisode(ProcessedEpisode O)
            : base(O)
        {
            this.NextToAir = O.NextToAir;
            this.EpNum2 = O.EpNum2;
            this.Ignore = O.Ignore;
            this.SI = O.SI;
            this.OverallNumber = O.OverallNumber;
        }

        public ProcessedEpisode(Episode e, ShowItem si)
            : base(e)
        {
            this.OverallNumber = -1;
            this.NextToAir = false;
            this.EpNum2 = this.EpNum;
            this.Ignore = false;
            this.SI = si;
        }

        public string NumsAsString()
        {
            if (this.EpNum == this.EpNum2)
                return this.EpNum.ToString();
            else
                return this.EpNum + "-" + this.EpNum2;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public static int EPNumberSorter(ProcessedEpisode e1, ProcessedEpisode e2)
        {
            int ep1 = e1.EpNum;
            int ep2 = e2.EpNum;

            return ep1 - ep2;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public static int DVDOrderSorter(ProcessedEpisode e1, ProcessedEpisode e2)
        {
            int ep1 = e1.EpNum;
            int ep2 = e2.EpNum;

            string key = "DVD_episodenumber";
            if (e1.Items.ContainsKey(key) && e2.Items.ContainsKey(key))
            {
                string n1 = e1.Items[key];
                string n2 = e2.Items[key];
                if ((!string.IsNullOrEmpty(n1)) && (!string.IsNullOrEmpty(n2)))
                {
                    try
                    {
                        int t1 = (int) (1000.0 * double.Parse(n1));
                        int t2 = (int) (1000.0 * double.Parse(n2));
                        ep1 = t1;
                        ep2 = t2;
                    }
                    catch (FormatException)
                    {
                    }
                }
            }

            return ep1 - ep2;
        }
    }

    public class ShowItem
    {
        public bool AutoAddNewSeasons;
        public string AutoAdd_FolderBase; // TODO: use magical renaming tokens here
        public bool AutoAdd_FolderPerSeason;
        public string AutoAdd_SeasonFolderName; // TODO: use magical renaming tokens here

        public bool CountSpecials;
        public string CustomShowName;
        public bool DVDOrder; // sort by DVD order, not the default sort we get
        public bool DoMissingCheck;
        public bool DoRename;
        public bool ForceCheckFuture;
        public bool ForceCheckNoAirdate;
        public System.Collections.Generic.List<int> IgnoreSeasons;
        public System.Collections.Generic.Dictionary<int, List<String>> ManualFolderLocations;
        public bool PadSeasonToTwoDigits;
        public System.Collections.Generic.Dictionary<int, List<ProcessedEpisode>> SeasonEpisodes; // built up by applying rules.
        public System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<ShowRule>> SeasonRules;
        public bool ShowNextAirdate;
        public int TVDBCode;
        public bool UseCustomShowName;
        public bool UseSequentialMatch;
        public List<string> AliasNames = new List<string>();
        public String CustomSearchURL;

        private DateTime? bannersLastUpdatedOnDisk;
        public DateTime? BannersLastUpdatedOnDisk
        {
            get
            {
                return bannersLastUpdatedOnDisk;
            }
            set
            {
                bannersLastUpdatedOnDisk = value;
            }
        }

        public ShowItem()
        {
            this.SetDefaults();
        }

        public ShowItem(int tvDBCode)
        {
            this.SetDefaults();
            this.TVDBCode = tvDBCode;
        }

        public ShowItem(XmlReader reader)
        {
            this.SetDefaults();

            reader.Read();
            if (reader.Name != "ShowItem")
                return; // bail out

            reader.Read();
            while (!reader.EOF)
            {
                if ((reader.Name == "ShowItem") && !reader.IsStartElement())
                    break; // all done

                if (reader.Name == "ShowName")
                {
                    this.CustomShowName = reader.ReadElementContentAsString();
                    this.UseCustomShowName = true;
                }
                if (reader.Name == "UseCustomShowName")
                    this.UseCustomShowName = reader.ReadElementContentAsBoolean();
                if (reader.Name == "CustomShowName")
                    this.CustomShowName = reader.ReadElementContentAsString();
                else if (reader.Name == "TVDBID")
                    this.TVDBCode = reader.ReadElementContentAsInt();
                else if (reader.Name == "CountSpecials")
                    this.CountSpecials = reader.ReadElementContentAsBoolean();
                else if (reader.Name == "ShowNextAirdate")
                    this.ShowNextAirdate = reader.ReadElementContentAsBoolean();
                else if (reader.Name == "AutoAddNewSeasons")
                    this.AutoAddNewSeasons = reader.ReadElementContentAsBoolean();
                else if (reader.Name == "FolderBase")
                    this.AutoAdd_FolderBase = reader.ReadElementContentAsString();
                else if (reader.Name == "FolderPerSeason")
                    this.AutoAdd_FolderPerSeason = reader.ReadElementContentAsBoolean();
                else if (reader.Name == "SeasonFolderName")
                    this.AutoAdd_SeasonFolderName = reader.ReadElementContentAsString();
                else if (reader.Name == "DoRename")
                    this.DoRename = reader.ReadElementContentAsBoolean();
                else if (reader.Name == "DoMissingCheck")
                    this.DoMissingCheck = reader.ReadElementContentAsBoolean();
                else if (reader.Name == "DVDOrder")
                    this.DVDOrder = reader.ReadElementContentAsBoolean();
                else if (reader.Name == "CustomSearchURL")
                    this.CustomSearchURL = reader.ReadElementContentAsString();
                else if (reader.Name == "ForceCheckAll") // removed 2.2.0b2
                    this.ForceCheckNoAirdate = this.ForceCheckFuture = reader.ReadElementContentAsBoolean();
                else if (reader.Name == "ForceCheckFuture")
                    this.ForceCheckFuture = reader.ReadElementContentAsBoolean();
                else if (reader.Name == "ForceCheckNoAirdate")
                    this.ForceCheckNoAirdate = reader.ReadElementContentAsBoolean();
                else if (reader.Name == "PadSeasonToTwoDigits")
                    this.PadSeasonToTwoDigits = reader.ReadElementContentAsBoolean();
                else if (reader.Name == "BannersLastUpdatedOnDisk")
                {
                    if (!reader.IsEmptyElement)
                    {

                        this.BannersLastUpdatedOnDisk = reader.ReadElementContentAsDateTime();
                    }else
                    reader.Read();
                }

                else if (reader.Name == "UseSequentialMatch")
                    this.UseSequentialMatch = reader.ReadElementContentAsBoolean();
                else if (reader.Name == "IgnoreSeasons")
                {
                    if (!reader.IsEmptyElement)
                    {
                        reader.Read();
                        while (reader.Name != "IgnoreSeasons")
                        {
                            if (reader.Name == "Ignore")
                                this.IgnoreSeasons.Add(reader.ReadElementContentAsInt());
                            else
                                reader.ReadOuterXml();
                        }
                    }
                    reader.Read();
                }
                else if (reader.Name == "AliasNames")
                {
                    if (!reader.IsEmptyElement)
                    {
                        reader.Read();
                        while (reader.Name != "AliasNames")
                        {
                            if (reader.Name == "Alias")
                                this.AliasNames.Add(reader.ReadElementContentAsString());
                            else
                                reader.ReadOuterXml();
                        }
                    }
                    reader.Read();
                }
                else if (reader.Name == "Rules")
                {
                    if (!reader.IsEmptyElement)
                    {
                        int snum = int.Parse(reader.GetAttribute("SeasonNumber"));
                        this.SeasonRules[snum] = new List<ShowRule>();
                        reader.Read();
                        while (reader.Name != "Rules")
                        {
                            if (reader.Name == "Rule")
                            {
                                this.SeasonRules[snum].Add(new ShowRule(reader.ReadSubtree()));
                                reader.Read();
                            }
                        }
                    }
                    reader.Read();
                }
                else if (reader.Name == "SeasonFolders")
                {
                    if (!reader.IsEmptyElement)
                    {
                        int snum = int.Parse(reader.GetAttribute("SeasonNumber"));
                        this.ManualFolderLocations[snum] = new List<String>();
                        reader.Read();
                        while (reader.Name != "SeasonFolders")
                        {
                            if ((reader.Name == "Folder") && reader.IsStartElement())
                            {
                                string ff = reader.GetAttribute("Location");
                                if (this.AutoFolderNameForSeason(snum) != ff)
                                    this.ManualFolderLocations[snum].Add(ff);
                            }
                            reader.Read();
                        }
                    }
                    reader.Read();
                }

                else
                    reader.ReadOuterXml();
            } // while
        }

        public SeriesInfo TheSeries()
        {
            return TheTVDB.Instance.GetSeries(this.TVDBCode);
        }

        public string ShowName
        {
            get
            {
                if (this.UseCustomShowName)
                    return this.CustomShowName;
                SeriesInfo ser = this.TheSeries();
                if (ser != null)
                    return ser.Name;
                return "<" + this.TVDBCode + " not downloaded>";
            }
        }

        public List<String> getSimplifiedPossibleShowNames()
        {
            List<String> possibles = new List<String>();

            String simplifiedShowName = Helpers.SimplifyName(this.ShowName);
            if (!(simplifiedShowName == "")) { possibles.Add( simplifiedShowName); }

            //Check the custom show name too
            if (this.UseCustomShowName)
            {
                String simplifiedCustomShowName = Helpers.SimplifyName(this.CustomShowName);
                if (!(simplifiedCustomShowName == "")) { possibles.Add(simplifiedCustomShowName); }
            }

            //Also add the aliases provided
            possibles.AddRange(from alias in this.AliasNames select Helpers.SimplifyName(alias));

            return possibles;

        }

        public string ShowStatus
        {
            get{
                SeriesInfo ser = this.TheSeries();
                if (ser != null ) return ser.getStatus();
                return "Unknown";
            }
        }

        public enum ShowAirStatus
        {
            NoEpisodesOrSeasons,
            Aired,
            PartiallyAired,
            NoneAired
        }

        public ShowAirStatus SeasonsAirStatus
        {
            get
            {
                if (HasSeasonsAndEpisodes)
                {
                    if (HasAiredEpisodes && !HasUnairedEpisodes)
                    {
                        return ShowAirStatus.Aired;
                    }
                    else if (HasUnairedEpisodes && !HasAiredEpisodes)
                    {
                        return ShowAirStatus.NoneAired;
                    }
                    else if (HasAiredEpisodes && HasUnairedEpisodes)
                    {
                        return ShowAirStatus.PartiallyAired;
                    }
                    else
                    {
                        //System.Diagnostics.Debug.Assert(false, "That is weird ... we have 'seasons and episodes' but none are aired, nor unaired. That case shouldn't actually occur !");
                        return ShowAirStatus.NoEpisodesOrSeasons;
                    }
                }
                else
                {
                    return ShowAirStatus.NoEpisodesOrSeasons;
                }
            }
        }

        bool HasSeasonsAndEpisodes
        {
            get
            {
                if (this.TheSeries() != null && this.TheSeries().Seasons != null && this.TheSeries().Seasons.Count > 0)
                {
                    foreach (KeyValuePair<int, Season> s in this.TheSeries().Seasons)
                    {
                        if(this.IgnoreSeasons.Contains(s.Key))
                            continue;
                        if (s.Value.Episodes != null && s.Value.Episodes.Count > 0)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        bool HasUnairedEpisodes
        {
            get
            {
                if (HasSeasonsAndEpisodes)
                {
                    foreach (KeyValuePair<int, Season> s in this.TheSeries().Seasons)
                    {
                        if(this.IgnoreSeasons.Contains(s.Key))
                            continue;
                        if (s.Value.Status == Season.SeasonStatus.NoneAired || s.Value.Status == Season.SeasonStatus.PartiallyAired)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        bool HasAiredEpisodes
        {
            get
            {
                if (HasSeasonsAndEpisodes)
                {
                    foreach (KeyValuePair<int, Season> s in this.TheSeries().Seasons)
                    {
                        if(this.IgnoreSeasons.Contains(s.Key))
                            continue;
                        if (s.Value.Status == Season.SeasonStatus.PartiallyAired || s.Value.Status == Season.SeasonStatus.Aired)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        }


        public string[] Genres
        {
            get
            {
                return this.TheSeries()?.GetGenres();
            }
        }

        public void SetDefaults()
        {
            this.ManualFolderLocations = new Dictionary<int, List<string>>();
            this.IgnoreSeasons = new List<int>();
            this.UseCustomShowName = false;
            this.CustomShowName = "";
            this.UseSequentialMatch = false;
            this.SeasonRules = new Dictionary<int, List<ShowRule>>();
            this.SeasonEpisodes = new Dictionary<int, List<ProcessedEpisode>>();
            this.ShowNextAirdate = true;
            this.TVDBCode = -1;
            //                WhichSeasons = gcnew List<int>;
            //                NamingStyle = (int)NStyle.DefaultStyle();
            this.AutoAddNewSeasons = true;
            this.PadSeasonToTwoDigits = false;
            this.AutoAdd_FolderBase = "";
            this.AutoAdd_FolderPerSeason = true;
            this.AutoAdd_SeasonFolderName = "Season ";
            this.DoRename = true;
            this.DoMissingCheck = true;
            this.CountSpecials = false;
            this.DVDOrder = false;
            CustomSearchURL = "";
            ForceCheckNoAirdate = false;
            ForceCheckFuture = false;
            this.BannersLastUpdatedOnDisk = null; //assume that the baners are old and have expired

        }

        public List<ShowRule> RulesForSeason(int n)
        {
            if (this.SeasonRules.ContainsKey(n))
                return this.SeasonRules[n];
            else
                return null;
        }

        public string AutoFolderNameForSeason(int n)
        {
            bool leadingZero = TVSettings.Instance.LeadingZeroOnSeason || this.PadSeasonToTwoDigits;
            string r = this.AutoAdd_FolderBase;
            if (string.IsNullOrEmpty(r))
                return "";

            if (!r.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString()))
                r += System.IO.Path.DirectorySeparatorChar.ToString();
            if (this.AutoAdd_FolderPerSeason)
            {
                if (n == 0)
                    r += TVSettings.Instance.SpecialsFolderName;
                else
                {
                    r += this.AutoAdd_SeasonFolderName;
                    if ((n < 10) && leadingZero)
                        r += "0";
                    r += n.ToString();
                }
            }
            return r;
        }

        public int MaxSeason()
        {
            int max = 0;
            foreach (KeyValuePair<int, List<ProcessedEpisode>> kvp in this.SeasonEpisodes)
            {
                if (kvp.Key > max)
                    max = kvp.Key;
            }
            return max;
        }

        //StringNiceName(int season)
        //{
        //    // something like "Simpsons (S3)"
        //    return String.Concat(ShowName," (S",season,")");
        //}

        public void WriteXMLSettings(XmlWriter writer)
        {
            writer.WriteStartElement("ShowItem");

            XMLHelper.WriteElementToXML(writer,"UseCustomShowName",this.UseCustomShowName);
            XMLHelper.WriteElementToXML(writer,"CustomShowName",this.CustomShowName);
            XMLHelper.WriteElementToXML(writer,"ShowNextAirdate",this.ShowNextAirdate);
            XMLHelper.WriteElementToXML(writer,"TVDBID",this.TVDBCode);
            XMLHelper.WriteElementToXML(writer,"AutoAddNewSeasons",this.AutoAddNewSeasons);
            XMLHelper.WriteElementToXML(writer,"FolderBase",this.AutoAdd_FolderBase);
            XMLHelper.WriteElementToXML(writer,"FolderPerSeason",this.AutoAdd_FolderPerSeason);
            XMLHelper.WriteElementToXML(writer,"SeasonFolderName",this.AutoAdd_SeasonFolderName);
            XMLHelper.WriteElementToXML(writer,"DoRename",this.DoRename);
            XMLHelper.WriteElementToXML(writer,"DoMissingCheck",this.DoMissingCheck);
            XMLHelper.WriteElementToXML(writer,"CountSpecials",this.CountSpecials);
            XMLHelper.WriteElementToXML(writer,"DVDOrder",this.DVDOrder);
            XMLHelper.WriteElementToXML(writer,"ForceCheckNoAirdate",this.ForceCheckNoAirdate);
            XMLHelper.WriteElementToXML(writer,"ForceCheckFuture",this.ForceCheckFuture);
            XMLHelper.WriteElementToXML(writer,"UseSequentialMatch",this.UseSequentialMatch);
            XMLHelper.WriteElementToXML(writer,"PadSeasonToTwoDigits",this.PadSeasonToTwoDigits);
            XMLHelper.WriteElementToXML(writer, "BannersLastUpdatedOnDisk", this.BannersLastUpdatedOnDisk);


            writer.WriteStartElement("IgnoreSeasons");
            foreach (int i in this.IgnoreSeasons)
            {
                XMLHelper.WriteElementToXML(writer,"Ignore",i);
            }
            writer.WriteEndElement();

            writer.WriteStartElement("AliasNames");
            foreach (string str in this.AliasNames)
            {
                XMLHelper.WriteElementToXML(writer,"Alias",str);
            }
            writer.WriteEndElement();

            XMLHelper.WriteElementToXML(writer, "CustomSearchURL",this.CustomSearchURL);

            foreach (KeyValuePair<int, List<ShowRule>> kvp in this.SeasonRules)
            {
                if (kvp.Value.Count > 0)
                {
                    writer.WriteStartElement("Rules");
                    XMLHelper.WriteAttributeToXML(writer ,"SeasonNumber",kvp.Key);

                    foreach (ShowRule r in kvp.Value)
                        r.WriteXML(writer);

                    writer.WriteEndElement(); // Rules
                }
            }
            foreach (KeyValuePair<int, List<String>> kvp in this.ManualFolderLocations)
            {
                if (kvp.Value.Count > 0)
                {
                    writer.WriteStartElement("SeasonFolders");

                    XMLHelper.WriteAttributeToXML(writer,"SeasonNumber",kvp.Key);

                    foreach (string s in kvp.Value)
                    {
                        writer.WriteStartElement("Folder");
                        XMLHelper.WriteAttributeToXML(writer,"Location",s);
                        writer.WriteEndElement(); // Folder
                    }

                    writer.WriteEndElement(); // Rules
                }
            }

            writer.WriteEndElement(); // ShowItem
        }

        public static List<ProcessedEpisode> ProcessedListFromEpisodes(List<Episode> el, ShowItem si)
        {
            List<ProcessedEpisode> pel = new List<ProcessedEpisode>();
            foreach (Episode e in el)
                pel.Add(new ProcessedEpisode(e, si));
            return pel;
        }

        public Dictionary<int, List<string>> AllFolderLocations()
        {
            return this.AllFolderLocations( true);
        }

        public static string TTS(string s) // trim trailing slash
        {
            return s.TrimEnd(System.IO.Path.DirectorySeparatorChar);
        }

        public Dictionary<int, List<string>> AllFolderLocations(bool manualToo)
        {
            Dictionary<int, List<string>> fld = new Dictionary<int, List<string>>();

            if (manualToo)
            {
                foreach (KeyValuePair<int, List<string>> kvp in this.ManualFolderLocations)
                {
                    if (!fld.ContainsKey(kvp.Key))
                        fld[kvp.Key] = new List<String>();
                    foreach (string s in kvp.Value)
                        fld[kvp.Key].Add(TTS(s));
                }
            }

            if (this.AutoAddNewSeasons && (!string.IsNullOrEmpty(this.AutoAdd_FolderBase)))
            {
                int highestThereIs = -1;
                foreach (KeyValuePair<int, List<ProcessedEpisode>> kvp in this.SeasonEpisodes)
                {
                    if (kvp.Key > highestThereIs)
                        highestThereIs = kvp.Key;
                }
                foreach (int i in SeasonEpisodes.Keys)
                {
                    if (this.IgnoreSeasons.Contains(i))
                        continue;

                    string newName = this.AutoFolderNameForSeason(i);
                    if ((!string.IsNullOrEmpty(newName)) && (Directory.Exists(newName)))
                    {
                        if (!fld.ContainsKey(i))
                            fld[i] = new List<String>();
                        if (!fld[i].Contains(newName))
                            fld[i].Add(TTS(newName));
                    }
                }
            }

            return fld;
        }

        public static int CompareShowItemNames(ShowItem one, ShowItem two)
        {
            string ones = one.ShowName; // + " " +one->SeasonNumber.ToString("D3");
            string twos = two.ShowName; // + " " +two->SeasonNumber.ToString("D3");
            return ones.CompareTo(twos);
        }
    }
}
