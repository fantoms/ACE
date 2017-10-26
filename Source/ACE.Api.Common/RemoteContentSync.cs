using ACE.Common;
using ACE.Database;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security;
using System.Security.Permissions;
using System.Text;
using System.Threading.Tasks;

namespace ACE.Api.Common
{
    /// <summary>
    /// Remote Content sync is used to download content from the Github Api.
    /// </summary>
    public static class RemoteContentSync
    {
        private static WorldDatabase WorldDb { get; set; }

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
        /// The amount of calls left before being rate limited.
        /// </summary>
        public static int TotalApiCallsAvailable { get; set; } = 60;

        /// <summary>
        /// The amount of calls left before being rate limited.
        /// </summary>
        public static int RemaingApiCalls { get; set; } = 60;

        /// <summary>
        /// The time when the Github API will accept more requests.
        /// </summary>
        public static DateTime? ApiResetTIme { get; set; } = DateTime.Today.AddYears(1);

        /// <summary>
        /// Default database names.
        /// </summary>
        private static readonly ReadOnlyCollection<string> DefaultDatabaseNames = new ReadOnlyCollection<string>(new[] { "ace_auth", "ace_shard", "ace_world" });

        public static void Initialize()
        {
            WorldDb = new WorldDatabase();
            WorldDb.Initialize(ConfigManager.Config.MySql.World.Host,
                          ConfigManager.Config.MySql.World.Port,
                          ConfigManager.Config.MySql.World.Username,
                          ConfigManager.Config.MySql.World.Password,
                          ConfigManager.Config.MySql.World.Database, false, false);
        }

        /// <summary>
        /// Captures the Rate Limit Values from the Response Header
        /// </summary>
        /// <param name="headers"></param>
        private static void StripHeaders(WebHeaderCollection headers)
        {
            if (headers?.Count > 0)
            {
                int tmpInt = 0;
                double RateLimitEpoch = 0;

                if (int.TryParse(headers.Get("X-RateLimit-Limit"), out tmpInt))
                {
                    TotalApiCallsAvailable = tmpInt;
                }
                if (int.TryParse(headers.Get("X-RateLimit-Remaining"), out tmpInt))
                {
                    RemaingApiCalls = tmpInt;
                }
                if (double.TryParse(headers.Get("X-RateLimit-Reset"), out RateLimitEpoch))
                {
                    ApiResetTIme = JwtUtil.ConvertFromUnixTimestamp(RateLimitEpoch);
                }
            }
        }

        /// <summary>
        /// Retreieves a string from a web location.
        /// </summary>
        public static string GetWebString(string updateUrl)
        {
            using (WebClient webClient = new WebClient())
            {
                var result = string.Empty;
                WebClient w = new WebClient();
                // Header is required for github
                w.Headers.Add("User-Agent", ApiUserAgent);
                try
                {
                    result = w.DownloadString(updateUrl);
                }
                catch
                {
                    return null;
                }
                finally
                {
                    StripHeaders(w.ResponseHeaders);
                }
                return (result);
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
        /// Retreives a file from a web location.
        /// </summary>
        public static bool GetWebContent(Tuple<string, string> download)
        {
            using (WebClient webClient = new WebClient())
            {
                var destinationPath = Path.GetFullPath(download.Item1);
                var sourceUrl = new Uri(download.Item2);
                WebClient w = new WebClient();
                // Header is required for github
                w.Headers.Add("User-Agent", ApiUserAgent);
                w.DownloadFileAsync(sourceUrl, destinationPath);
                if (File.Exists(destinationPath))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if a Path exists and creates one if it doesn't.
        /// </summary>
        private static bool CheckLocalDataPath(string dataPath)
        {
            // Check to verify config has a valid path:
            if (dataPath?.Length > 0)
            {
                var currentPath = Path.GetFullPath(dataPath);
                // Check too see if path exists and create if does not exist.
                if (!Directory.Exists(currentPath))
                {
                    try
                    {
                        Directory.CreateDirectory(currentPath);
                    }
                    catch
                    {
                        // Could not create directory
                        return false;
                    }
                }
                PermissionSet perms = new PermissionSet(PermissionState.None);
                FileIOPermission writePermission = new FileIOPermission(FileIOPermissionAccess.Write, currentPath);
                perms.AddPermission(writePermission);
                if (!perms.IsSubsetOf(AppDomain.CurrentDomain.PermissionSet))
                {
                    // You don't have write permissions
                    return false;
                }
                // All checks pass, so the directory is good to use.
                return true;
            }
            // Config is error
            return false;
        }

        /// <summary>
        /// Attempts to retrieve contents from a Github directory/folder structure.
        /// </summary>
        /// <param name="url">String value containing web location to parse from the Github API.</param>
        /// <returns>true on success, false on failure</returns>
        public static bool RetreiveGithubFolder(string url)
        {
            // Check to see if the input is usable and the data path is valid
            if (url?.Length > 0 && ConfigManager.Config.ContentServer.LocalDataPath?.Length > 0)
            {
                var localDataPath = Path.GetFullPath(ConfigManager.Config.ContentServer.LocalDataPath);
                // Test the download path, to verify we can download:
                if (CheckLocalDataPath(ConfigManager.Config.ContentServer.LocalDataPath))
                {
                    List<string> directoryUrls = new List<string>();
                    directoryUrls.Add(url);
                    var downloads = new List<Tuple<string, string>>();
                    // Recurse api and collect all downloads
                    while (directoryUrls.Count > 0)
                    {
                        var currentUrl = directoryUrls.LastOrDefault();
                        var repoFiles = JArray.Parse(GetWebString(currentUrl));
                        if (repoFiles?.Count > 0)
                        {
                            foreach (var file in repoFiles)
                            {
                                if (file["type"].ToString() == "dir")
                                {
                                    directoryUrls.Add(file["url"].ToString());
                                    CheckLocalDataPath(Path.Combine(localDataPath, file["path"].ToString()));
                                }
                                else
                                {
                                    downloads.Add(new Tuple<string, string>(item1: Path.Combine(localDataPath, file["path"].ToString()), item2: file["download_url"].ToString()));
                                }
                            }
                        }
                        // Cancel because the string was caught in exception
                        if (repoFiles == null)
                            return false;
                        // Remove the parsed url
                        directoryUrls.Remove(currentUrl);
                    }
                    // Download the files from the downloads tuple
                    foreach (var download in downloads)
                    {
                        // If we cannot retreive content, return false for failure
                        if (!GetWebContent(download))
                            return false;
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Creates a webClient that connects too Github and extracts relevant download metadata.
        /// </summary>
        public static bool RetreieveWorldData()
        {
            if (RemaingApiCalls < TotalApiCallsAvailable)
            {
                string GithubDownload = "";
                string GithubFilename = "";
                // attempt to download the latest ACE-World json data
                try
                {
                    using (WebClient webClient = new WebClient())
                    {
                        WebClient w = new WebClient();
                        // Header is required for github
                        w.Headers.Add("User-Agent", "ACEManager");
                        var json = JObject.Parse(w.DownloadString(ConfigManager.Config.ContentServer.WorldArchiveUrl));
                        // Extract relevant details
                        GithubDownload = (string)json["assets"][0]["browser_download_url"];
                        GithubFilename = (string)json["assets"][0]["name"];
                        //(string)json["name"] + (string)json["tag_name"] + (string)json["published_at"];
                        // Collect header info that tells how much retries and time left till reset.
                        StripHeaders(w.ResponseHeaders);
                    }
                }
                catch (Exception error)
                {
                    // Log failure
                    return false;
                }
                var WordArchive = Path.GetFullPath(Path.Combine(ConfigManager.Config.ContentServer.LocalDataPath, GithubFilename));

                if (GetWebContent(GithubDownload, WordArchive))
                {
                    // Extract & delete
                    try
                    {
                        ExtractZip(WordArchive, Path.GetFullPath(Path.Combine(ConfigManager.Config.ContentServer.LocalDataPath, "Database\\ACE-World")));
                    }
                    catch
                    {
                        // issue with disk space, trouble extracting, or file path became invalid
                        return false;
                    }
                }
                return true;
            }
            // No more calls left, detail the time remaining
            return false;
        }

        /// <summary>
        /// Attempts to Load all world data present from the appropriate downloaded folder, into a name database with the name collected from the config.
        /// </summary>
        public static bool ReLoadWorld()
        {
            Initialize();

            if (ConfigManager.Config.MySql.World.Database?.Length > 0 && ConfigManager.Config.ContentServer.LocalDataPath?.Length > 0)
            {
                var databaseName = ConfigManager.Config.MySql.World.Database;

                // Delete Database, to clear everything including stored procs and views.
                WorldDb.DropDatabase(databaseName);

                // Create Database
                WorldDb.CreateDatabase(databaseName);

                // Load World Data from 3 locations, in sequential order:
                //  First Search Path: Database\\Base\\World\\

                var worldBase = Path.GetFullPath(Path.Combine(ConfigManager.Config.ContentServer.LocalDataPath, "Database\\Base\\WorldBase.sql"));
                if (File.Exists(worldBase))
                    LoadScript(worldBase, databaseName);
                else
                    return false;

                //  Second Search Path: Database\\Updates\\World\\
                //  Third Search Path: Database\\ACE-World\\
                var worldDataPaths = new List<string> {                    
                    Path.GetFullPath(Path.Combine(ConfigManager.Config.ContentServer.LocalDataPath, "Database\\Updates\\World\\")),
                    Path.GetFullPath(Path.Combine(ConfigManager.Config.ContentServer.LocalDataPath, "Database\\ACE-World\\"))
                };

                foreach (var worldUpdatePath in worldDataPaths)
                {
                    try
                    {
                        var files = from file in Directory.EnumerateFiles(worldUpdatePath) where !file.Contains(".txt") select new { File = file };
                        if (files.Count() > 0)
                        {
                            foreach (var file in files)
                            {
                                string sqlFile = file.File;
                                // find the base file:
                                if (!File.Exists(sqlFile))
                                {
                                    //$"Cannot locate ACE-World Data, please click download!";
                                    return false;
                                }
                                else
                                {
                                    LoadScript(file.File, databaseName);
                                }
                            }
                        }
                    }
                    catch (Exception error)
                    {
                        return false;
                    }
                }
                // Success
                return true;
            }
            // Could not find configuration or error in function.
            return false;
        }

        public static void LoadScript(string sqlFile, string databaseName)
        {
            // open fild into string
            //"Loading ACE-World, may take quite awhile (please wait)!...";
            string sqlInputFile = File.ReadAllText(sqlFile);
            if (!DefaultDatabaseNames.Contains(databaseName))
            {
                if (DefaultDatabaseNames.Any(sqlInputFile.Contains))
                {
                    // Default Detabase should be ace_world:
                    sqlInputFile = sqlInputFile.Replace(DefaultDatabaseNames[2], databaseName);
                }
            }
            var result = WorldDb.ExecuteScript(sqlInputFile, databaseName);
            if (result.Length > 0)
            {
                //result;
            }
        }

        /// <summary>
        /// Attempts to extract a file from a directory, into a relative path. If ACEManager.Config.SaveOldWorldArchives is false, then the archive will also be deleted.
        /// </summary>
        private static void ExtractZip(string filePath, string destinationPath)
        {
            // $"Extracting Zip {filePath}...";
            if (Directory.Exists(destinationPath)) Directory.Delete(destinationPath, true);
            Directory.CreateDirectory(destinationPath);
            if (!File.Exists(filePath))
            {
                // $"ERROR: Zip missing!";
                return;
            }

            try
            {
                ZipFile.ExtractToDirectory(filePath, destinationPath);
            }
            catch (Exception error)
            {
                // error.Message;
                return;
            }
            finally
            {
                // $"Deleting archive {filePath}";
                File.Delete(filePath);
            }
        }
    }
}
