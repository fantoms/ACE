using ACE.Common;
using ACE.Database;
using log4net;
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

namespace ACE.Database
{
    /// <summary>
    /// Remote Content sync is used to download content from the Github Api.
    /// </summary>
    public static class RemoteContentSync
    {
        private static ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private static WorldDatabase WorldDb { get; set; }

        /// <summary>
        /// External user agent used when connecting to the github Api, or another location.
        /// </summary>
        private static string ApiUserAgent { get; set; } = "ACEmulator.Api";

        /// <summary>
        /// Url to download the latest version of ACE-World.
        /// </summary>
        private static string WorldGithubDownload { get; set; }

        /// <summary>
        /// Filename for the ACE-World Release.
        /// </summary>
        private static string WorldGithubFilename { get; set; }

        /// <summary>
        /// Local path pointing too the Extracted ACE-World Data.
        /// </summary>
        private static string WorldDataPath { get; set; } = "Database\\ACE-World\\";

        /// <summary>
        /// Database/Updates/World/
        /// </summary>
        private static string WoldGithubUpdatePath { get; set; } = "Database\\Updates\\World\\";

        /// <summary>
        /// Database/Base/World/
        /// </summary>
        private static string WoldGithubBaseSqlPath { get; set; } = "Database\\Base\\";

        /// <summary>
        /// WorldBase.sql
        /// </summary>
        private static string WoldGithubBaseSqlFile { get; set; } = "WorldBase.sql";

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
        private static void CaptureWebHeaderData(WebHeaderCollection headers)
        {
            if (headers?.Count > 0)
            {
                int tmpInt = 0;
                double RateLimitEpoch = 0;
                // capture the total api calls
                if (int.TryParse(headers.Get("X-RateLimit-Limit"), out tmpInt))
                {
                    TotalApiCallsAvailable = tmpInt;
                }
                // capture the remaining api calls
                if (int.TryParse(headers.Get("X-RateLimit-Remaining"), out tmpInt))
                {
                    RemaingApiCalls = tmpInt;
                }
                // capture the timestamp for rate limite reset
                if (double.TryParse(headers.Get("X-RateLimit-Reset"), out RateLimitEpoch))
                {
                    ApiResetTIme = ConvertFromUnixTimestamp(RateLimitEpoch);
                }
            }
        }

        /// <summary>
        /// Retreieves a string from a web location.
        /// </summary>
        public static string RetrieveWebString(string updateUrl)
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
                    CaptureWebHeaderData(w.ResponseHeaders);
                }
                return (result);
            }
        }

        /// <summary>
        /// Retrieves a file from a web location.
        /// </summary>
        private static bool RetrieveWebContent(string url, string destinationFilePath)
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
            log.Debug($"Troubles downloading {url} {destinationFilePath}");
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
                        log.Debug($"Could not create directory: {dataPath}");
                        return false;
                    }
                }
                PermissionSet perms = new PermissionSet(PermissionState.None);
                FileIOPermission writePermission = new FileIOPermission(FileIOPermissionAccess.Write, currentPath);
                perms.AddPermission(writePermission);
                if (!perms.IsSubsetOf(AppDomain.CurrentDomain.PermissionSet))
                {
                    // You don't have write permissions
                    log.Debug($"Write permissions missing in: {dataPath}");
                    return false;
                }
                // All checks pass, so the directory is good to use.
                return true;
            }
            log.Debug($"Configuration error, missing datapath!");
            return false;
        }

        /// <summary>
        /// Attempts to retrieve contents from a Github directory/folder structure.
        /// </summary>
        /// <param name="url">String value containing web location to parse from the Github API.</param>
        /// <returns>true on success, false on failure</returns>
        public static bool RetrieveGithubFolder(string url)
        {
            // Check to see if the input is usable and the data path is valid
            if (url?.Length > 0)
            {
                var localDataPath = Path.GetFullPath(ConfigManager.Config.ContentServer.LocalDataPath);
                List<string> directoryUrls = new List<string>();
                directoryUrls.Add(url);
                var downloads = new List<Tuple<string, string>>();
                // Recurse api and collect all downloads
                while (directoryUrls.Count > 0)
                {
                    var currentUrl = directoryUrls.LastOrDefault();
                    var repoFiles = JArray.Parse(RetrieveWebString(currentUrl));
                    var repoPath = Path.Combine(localDataPath, Path.GetDirectoryName(repoFiles[0]["path"].ToString()));
                    CheckLocalDataPath(repoPath);
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
                                downloads.Add(new Tuple<string, string>(item1: file["download_url"].ToString(), item2: Path.Combine(localDataPath, file["path"].ToString())));
                            }
                        }
                    }
                    // Cancel because the string was caught in exception
                    if (repoFiles == null)
                    {
                        log.Debug($"No files found within {repoPath}");
                        return false;
                    }
                    // Remove the parsed url
                    directoryUrls.Remove(currentUrl);
                }
                // Download the files from the downloads tuple
                foreach (var download in downloads)
                {
                    // If we cannot Retrieve content, return false for failure
                    if (!RetrieveWebContent(download.Item1, download.Item2))
                        log.Debug($"Trouble downloading {download.Item1} : {download.Item2}");
                }
                return true;

            }
            log.Debug($"Invalid Url provided, please check configuration.");
            return false;
        }

        /// <summary>
        /// Creates a webClient that connects too Github and extracts relevant download metadata.
        /// </summary>
        public static string RetreieveWorldData()
        {
            if (RemaingApiCalls < TotalApiCallsAvailable)
            {
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
                        WorldGithubDownload = (string)json["assets"][0]["browser_download_url"];
                        WorldGithubFilename = (string)json["assets"][0]["name"];
                        //(string)json["name"] + (string)json["tag_name"] + (string)json["published_at"];
                        // Collect header info that tells how much retries and time left till reset.
                        CaptureWebHeaderData(w.ResponseHeaders);
                    }
                }
                catch (Exception error)
                {
                    log.Debug($"Trouble capturing metadata from the Github API. {error.ToString()}");
                    return error.Message;
                }
                var WordArchive = Path.GetFullPath(Path.Combine(ConfigManager.Config.ContentServer.LocalDataPath, WorldGithubFilename));

                if (RetrieveWebContent(WorldGithubDownload, WordArchive))
                {
                    // Extract & delete
                    var extractionError = ExtractZip(WordArchive, Path.GetFullPath(Path.Combine(ConfigManager.Config.ContentServer.LocalDataPath, "Database\\ACE-World\\")));
                    if (extractionError?.Length > 0)
                    {
                        log.Debug($"Could not extract {WordArchive} {extractionError}");
                        return $"Error Extracting {extractionError}";
                    }

                }
                return null;
            }
            // No more calls left, detail the time remaining
            var errorMessage = $"You have exhausted your Github API Limit limt per hour. Please wait till {ApiResetTIme}";
            log.Error(errorMessage);
            return $"You have exhausted your Github API Limit limt per hour. Please wait till {ApiResetTIme}";
        }

        /// <summary>
        /// Attempts to Load all world data present from the appropriate downloaded folder, into a name database with the name collected from the config.
        /// </summary>
        /// <remarks>
        /// This function is overwhelmed with complexity, due too the fact that we are merging around 4 different structures, using a SaaS (github).
        /// </remarks>
        public static string RedeployWorldDatabase()
        {
            log.Debug("A World Redeploy has been initiated.");
            // Determine if the config settings appear valid:
            if (ConfigManager.Config.MySql.World.Database?.Length > 0 && ConfigManager.Config.ContentServer.LocalDataPath?.Length > 0)
            {
                // Check the data path and create if needed.
                var localDataPath = ConfigManager.Config.ContentServer.LocalDataPath;
                if (CheckLocalDataPath(localDataPath))
                {
                    // Setup the database requirements.
                    Initialize();
                    // Download the database files from Github:
                    if (CheckLocalDataPath(Path.GetFullPath(Path.Combine(localDataPath, WoldGithubBaseSqlPath))))
                        RetrieveWebContent(ConfigManager.Config.ContentServer.WorldBaseUrl, Path.GetFullPath(Path.Combine(localDataPath, WoldGithubBaseSqlPath, WoldGithubBaseSqlFile)));
                    RetrieveGithubFolder(ConfigManager.Config.ContentServer.WorldUpdateUrl);
                    RetreieveWorldData();

                    var databaseName = ConfigManager.Config.MySql.World.Database;

                    // Delete Database, to clear everything including stored procs and views.
                    var dropResult = WorldDb.DropDatabase(databaseName);
                    if (dropResult != null)
                    {
                        log.Debug($"Error dropping database: {dropResult}");
                    }

                    // Create Database
                    var createResult = WorldDb.CreateDatabase(databaseName);
                    if (createResult != null)
                    {
                        log.Debug($"Error dropping database: {createResult}");
                    }

                    // Load World Data from 3 locations, in sequential order:
                    //  First Search Path: Base\\WorldBase.sql
                    //  Second Search Path: Updates\\World\\
                    //  Third Search Path: ACE-World\\${WorldGithubFilename}
                    var worldBase = Path.GetFullPath(Path.Combine(ConfigManager.Config.ContentServer.LocalDataPath, WoldGithubBaseSqlPath, WoldGithubBaseSqlFile));
                    var worldDataPath = Path.GetFullPath(Path.Combine(ConfigManager.Config.ContentServer.LocalDataPath, WorldDataPath));
                    var worldUpdatePath = Path.GetFullPath(Path.Combine(ConfigManager.Config.ContentServer.LocalDataPath, WoldGithubUpdatePath));

                    try
                    {
                        // First sequence, load the world base
                        if (File.Exists(worldBase))
                            ReadAndLoadScript(worldBase, databaseName);
                        else
                            return "There was an error locating the WorldBase.sql file!";

                        // Second, find all of the sql files in directory, and load them
                        var files = from file in Directory.EnumerateFiles(worldDataPath) where !file.Contains(".txt") select new { File = file };
                        if (files.Count() > 0)
                        {
                            foreach (var file in files)
                            {
                                ReadAndLoadScript(file.File, databaseName);
                            }
                        }

                        // Last, find all of the sql files in directory, and load them
                        files = from file in Directory.EnumerateFiles(worldUpdatePath) where !file.Contains(".txt") select new { File = file };
                        if (files.Count() > 0)
                        {
                            foreach (var file in files)
                            {
                                ReadAndLoadScript(file.File, databaseName);
                            }
                        }
                    }
                    catch (Exception error)
                    {
                        var errorMessage = error.Message;
                        if (error.InnerException != null)
                        {
                            errorMessage += " Inner: " + error.InnerException.Message;
                        }
                        log.Debug(errorMessage);
                        return errorMessage;
                    }
                    // Success
                    return null;
                }
                var invalidDownloadPath = "Invalid Download path.";
                log.Debug(invalidDownloadPath);
                return invalidDownloadPath;
            }
            // Could not find configuration or error in function.
            var configErrorMessage = "Could not find configuration or an unknown error has occurred.";
            log.Debug(configErrorMessage);
            return configErrorMessage;
        }

        private static string ReadAndLoadScript(string sqlFile, string databaseName)
        {
            Console.Write(Environment.NewLine + $"{databaseName} Loading {sqlFile}!..");
            log.Debug($"Reading {sqlFile} and executing against {databaseName}");
            // open file into string
            string sqlInputFile = File.ReadAllText(sqlFile);
            if (!DefaultDatabaseNames.Contains(databaseName))
            {
                if (DefaultDatabaseNames.Any(sqlInputFile.Contains))
                {
                    // Default Detabase should be ace_world:
                    sqlInputFile = sqlInputFile.Replace(DefaultDatabaseNames[2], databaseName);
                }
            }
            return WorldDb.ExecuteSqlQueryOrScript(sqlInputFile, databaseName, true);
        }

        /// <summary>
        /// Attempts to extract a file from a directory, into a relative path. If ACEManager.Config.SaveOldWorldArchives is false, then the archive will also be deleted.
        /// </summary>
        private static string ExtractZip(string filePath, string destinationPath)
        {
            // $"Extracting Zip {filePath}...";
            if (Directory.Exists(destinationPath)) Directory.Delete(destinationPath, true);
            Directory.CreateDirectory(destinationPath);
            if (!File.Exists(filePath))
            {
                return "ERROR: Zip missing!";
            }

            log.Debug($"Extracting archive {filePath}");
            try
            {
                ZipFile.ExtractToDirectory(filePath, destinationPath);
            }
            catch (Exception error)
            {
                return error.Message;
            }
            finally
            {
                log.Debug($"Deleting archive {filePath}");
                File.Delete(filePath);
            }
            return null;
        }

        /// <summary>
        /// Converts a double to epock time. Used with Github.
        /// </summary>
        public static DateTime ConvertFromUnixTimestamp(double unixTimeStamp)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc).AddSeconds(unixTimeStamp).ToLocalTime();
        }
    }
}
