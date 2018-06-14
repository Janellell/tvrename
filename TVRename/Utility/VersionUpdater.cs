using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace TVRename
{
    public static class VersionUpdater
    {
        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public static UpdateVersion CheckForUpdates()
        {
            const string GITHUB_RELEASES_API_URL = "https://api.github.com/repos/TV-Rename/tvrename/releases";
            UpdateVersion currentVersion;

            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;


            try
            {
                string currentVersionString = Helpers.DisplayVersion;

                bool inDebug = currentVersionString.EndsWith(" ** Debug Build **");
                //remove debug stuff
                if (inDebug)
                    currentVersionString = currentVersionString.Substring(0,
                        currentVersionString.LastIndexOf(" ** Debug Build **", StringComparison.Ordinal));

                currentVersion =
                    new UpdateVersion(currentVersionString, UpdateVersion.VersionType.Friendly);
            }
            catch (ArgumentException e)
            {
                logger.Error("Failed to establish if there are any new releases as could not parse internal version: " + Helpers.DisplayVersion, e);
                return null;

            }

            UpdateVersion latestVersion = null;
            UpdateVersion latestBetaVersion = null;

            try
            {

                WebClient client = new WebClient();
                client.Headers.Add("user-agent",
                    "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.36");
                string response = client.DownloadString(GITHUB_RELEASES_API_URL);
                JArray gitHubInfo = JArray.Parse(response);

                foreach (JObject gitHubReleaseJSON in gitHubInfo.Children<JObject>())
                {
                    try
                    {
                        DateTime.TryParse(gitHubReleaseJSON["published_at"].ToString(), out DateTime releaseDate);
                        UpdateVersion testVersion = new UpdateVersion(gitHubReleaseJSON["tag_name"].ToString(),
                            UpdateVersion.VersionType.Semantic)
                        {
                            DownloadUrl = gitHubReleaseJSON["assets"][0]["browser_download_url"].ToString(),
                            ReleaseNotesText = gitHubReleaseJSON["body"].ToString(),
                            ReleaseNotesUrl = gitHubReleaseJSON["html_url"].ToString(),
                            ReleaseDate = releaseDate,
                            IsBeta = (gitHubReleaseJSON["prerelease"].ToString() == "True")
                        };

                        //all versions want to be considered if you are in the beta stream
                        if (testVersion.NewerThan(latestBetaVersion)) latestBetaVersion = testVersion;

                        //If the latest version is a production one then update the latest production version
                        if (!testVersion.IsBeta)
                        {
                            if (testVersion.NewerThan(latestVersion)) latestVersion = testVersion;
                        }
                    }
                    catch (NullReferenceException ex)
                    {
                        logger.Warn("Looks like the JSON payload from GitHub has changed");
                        logger.Debug(ex, gitHubReleaseJSON.ToString());
                        continue;
                    }
                    catch (ArgumentOutOfRangeException ex)
                    {
                        logger.Debug("Generally happens because the release did not have an exe attached");
                        logger.Debug(ex, gitHubReleaseJSON.ToString());
                        continue;
                    }

                }
                if (latestVersion == null)
                {
                    logger.Error("Could not find latest version information from GitHub: {0}", response);
                    return null;
                }

                if (latestBetaVersion == null)
                {
                    logger.Error("Could not find latest beta version information from GitHub: {0}", response);
                    return null;
                }

            }
            catch (Exception e)
            {
                logger.Error(e, "Failed to contact GitHub to identify new releases");
                return null;

            }





            if ((TVSettings.Instance.mode == TVSettings.BetaMode.ProductionOnly) &&
                (latestVersion.NewerThan(currentVersion))) return latestVersion;

            if ((TVSettings.Instance.mode == TVSettings.BetaMode.BetaToo) &&
                (latestBetaVersion.NewerThan(currentVersion)))
                return latestBetaVersion;

            return null;
        }


    }
}


public class UpdateVersion : IComparable
{
    public string DownloadUrl { get; set; }
    public string ReleaseNotesText { get; set; }
    public string ReleaseNotesUrl { get; set; }
    public bool IsBeta { get; set; }
    public DateTime ReleaseDate { get; set; }

    public Version VersionNumber { get; }
    public string Prerelease { get; }
    public string Build { get; }

    public enum VersionType { Semantic, Friendly }

    public UpdateVersion(string version, VersionType type)
    {
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("The provided version string is invalid.", nameof(version));

        string matchString = (type == VersionType.Semantic)
            ? @"^(?<major>[0-9]+)((\.(?<minor>[0-9]+))(\.(?<patch>[0-9]+))?)?(\-(?<pre>[0-9A-Za-z\-\.]+|[*]))?(\+(?<build>[0-9A-Za-z\-\.]+|[*]))?$"
            : @"^(?<major>[0-9]+)((\.(?<minor>[0-9]+))(\.(?<patch>[0-9]+))?)?( (?<pre>[0-9A-Za-z\- \.]+))?$";

        Regex regex = new Regex(matchString, RegexOptions.ExplicitCapture);
        Match match = regex.Match(version);

        if (!match.Success || !match.Groups["major"].Success || !match.Groups["minor"].Success) throw new ArgumentException("The provided version string is invalid.", nameof(version));
        if (type == VersionType.Semantic && !match.Groups["patch"].Success) throw new ArgumentException("The provided version string is invalid semantic version.", nameof(version));

        this.VersionNumber = new Version(int.Parse(match.Groups["major"].Value),
            int.Parse(match.Groups["minor"].Value),
            match.Groups["patch"].Success ? int.Parse(match.Groups["patch"].Value) : 0);

        this.Prerelease = match.Groups["pre"].Value.Replace(" ", string.Empty);
        this.Build = match.Groups["build"].Value ?? string.Empty;
    }

    public int CompareTo(object obj)
    {

        if (obj == null) return 1;
        if (!(obj is UpdateVersion otherUpdateVersion)) throw new ArgumentException("Object is not a UpdateVersion");


        //Returns 1 if this > object, 0 if this=object and -1 if this< object


        //Extract Version Numbers and then compare them
        if (this.VersionNumber.CompareTo(otherUpdateVersion.VersionNumber) != 0) return this.VersionNumber.CompareTo(otherUpdateVersion.VersionNumber);

        //We have the same version - now we have to get tricky and look at the extension (rc1, beta2 etc)
        //if both have no extension then they are the same
        if (string.IsNullOrWhiteSpace(this.Prerelease) && string.IsNullOrWhiteSpace(otherUpdateVersion.Prerelease)) return 0;

        //If either are not present then you can assume they are FINAL versions and trump any rx1 verisons
        if (string.IsNullOrWhiteSpace(this.Prerelease)) return 1;
        if (string.IsNullOrWhiteSpace(otherUpdateVersion.Prerelease)) return -1;

        //We have 2 suffixes
        //Compare alphabetically alpha1 < alpha2 < beta1 < beta2 < rc1 < rc2 etc
        return (string.Compare(this.Prerelease, otherUpdateVersion.Prerelease, StringComparison.OrdinalIgnoreCase));
    }

    public bool NewerThan(UpdateVersion compare) => (CompareTo(compare) > 0);

    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append(this.VersionNumber);
        if (!string.IsNullOrWhiteSpace(this.Prerelease)) sb.Append("-" + this.Prerelease);
        if (!string.IsNullOrWhiteSpace(this.Build)) sb.Append("-(" + this.Build + ")");
        return sb.ToString();
    }

    public string LogMessage()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("************************");
        sb.AppendLine("**New Update Available**");
        sb.AppendLine("************************");
        sb.AppendLine($"A new verion is available: {ToString()} since {ReleaseDate}");
        sb.AppendLine($"please download from {DownloadUrl}");
        sb.AppendLine($"full notes available from {ReleaseNotesUrl}");
        sb.AppendLine(ReleaseNotesText);
        return sb.ToString();
    }
}
