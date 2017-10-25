using ACE.Common;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ACE.Api.Common
{
    /// <summary>
    /// Remote Content sync is used to download content from the Github Api.
    /// </summary>
    /// <remarks>
    /// Notes:
    ///   GetACEWorldMetaData();
    ///   GetLatestACEWorldData();
    ///   GetBaseSql();
    ///   GetAllUpdates();
    ///   ExtractZip();
    /// </remarks>
    public static class RemoteContentSync
    {
        /// <summary>
        /// External user agent used when connecting to the github Api, or another location.
        /// </summary>
        private static string ApiUserAgent { get; set; } = "ACEmulator.Api";

        /// <summary>
        /// Url to download the latest version of ACE-World.
        /// </summary>
        public static string WorldGithubDownload { get; set; }

        /// <summary>
        /// Filename for the ACE-World Release.
        /// </summary>
        public static string WorldGithubFilename { get; set; }

        /// <summary>
        /// Creates a webClient that connects too Github and extracts relevant download metadata.
        /// </summary>
        public static bool GetACEWorldMetaData()
        {
            var url = ConfigManager.Config.ContentServer.WorldArchiveUrl;
            // Must have a url preloaded
            if (url?.Length > 0)
            {
                // attempt to download the latest ACE-World json data
                try
                {
                    using (WebClient webClient = new WebClient())
                    {
                        WebClient w = new WebClient();
                        // Header is required for github
                        w.Headers.Add("User-Agent", ApiUserAgent);
                        var json = JObject.Parse(w.DownloadString(url));
                        // Extract relevant details
                        WorldGithubDownload = (string)json["assets"][0]["browser_download_url"];
                        WorldGithubFilename = (string)json["assets"][0]["name"];
                    }
                }
                catch (Exception error)
                {
                    return false;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Retreieves a string from a web location.
        /// </summary>
        public static string GetWebString(string updateUrl)
        {
            using (WebClient webClient = new WebClient())
            {
                WebClient w = new WebClient();
                // Header is required for github
                w.Headers.Add("User-Agent", ApiUserAgent);
                return (w.DownloadString(updateUrl));
            }
        }

        /// <summary>
        /// Retreives a file from a web location.
        /// </summary>
        public static bool GetWebContent(string url, string destinationFilePath)
        {
            using (WebClient webClient = new WebClient())
            {
                WebClient w = new WebClient();
                // Header is required for github
                w.Headers.Add("User-Agent", ApiUserAgent);
                w.DownloadFile(url, destinationFilePath);
                if (File.Exists(destinationFilePath))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Attempts to retrieve contents from a Github directory/folder structure.
        /// </summary>
        /// <param name="url">String value containing web location to parse from the Github API.</param>
        /// <returns>true on success, false on failure</returns>
        public static bool RetreiveGithubFolder(string url)
        {
            if (url?.Length > 0)
            {
                var folder = JArray.Parse(GetWebString(url));
                if (folder.Count > 0)
                {
                    return false;
                }
                return true;
            }
            return false;
        }

        public static void RetreieveMetadata()
        {
            
        }

        /// <summary>
        /// Retrieves the latest Updates from Github.
        /// </summary>
        //private void GetAllUpdates()
        //{
        //    var authfiles = JArray.Parse(GetWebString(ConfigManager.Config.AuthenticationUpdatesSqlUrl));
        //    foreach (var item in authfiles)
        //    {
        //        GetWebContent(item["download_url"].ToString(), Path.Combine(ConfigManager.DataPath, ACEManager.Config.UpdatesSqlPath, ACEManager.Config.AuthenticationUpdatesPath, item["name"].ToString()));
        //    }
        //    var shardfiles = JArray.Parse(GetWebString(ACEManager.Config.ShardUpdatesSqlUrl));
        //    foreach (var item in shardfiles)
        //    {
        //        GetWebContent(item["download_url"].ToString(), Path.Combine(ConfigManager.DataPath, ACEManager.Config.UpdatesSqlPath, ACEManager.Config.ShardUpdatesPath, item["name"].ToString()));
        //    }
        //    var worldfiles = JArray.Parse(GetWebString(ACEManager.Config.WorldUpdatesSqlUrl));
        //    foreach (var item in worldfiles)
        //    {
        //        GetWebContent(item["download_url"].ToString(), Path.Combine(ConfigManager.DataPath, ACEManager.Config.UpdatesSqlPath, ACEManager.Config.WorldUpdatesPath, item["name"].ToString()));
        //    }
        //}
    }
}
