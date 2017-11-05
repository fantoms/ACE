﻿using ACE.Common;
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

        private static Database CurrentDb { get; set; }

        public static bool RedeploymentActive { get; private set; } = false;

        /// <summary>
        /// External user agent used when connecting to the github Api, or another location.
        /// </summary>
        private static string ApiUserAgent { get; set; } = "ACEmulator";

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
        public static DateTime? ApiResetTime { get; set; } = DateTime.Today.AddYears(1);

        /// <summary>
        /// Default ACE Authentication database name.
        /// </summary>
        private static readonly string DefaultAuthenticationDatabaseName = "ace_auth";

        /// <summary>
        /// Default ACE Shard database name.
        /// </summary>
        private static readonly string DefaultShardDatabaseName = "ace_auth";

        /// <summary>
        /// Default ACE World database name.
        /// </summary>
        private static readonly string DefaultWorldDatabaseName = "ace_auth";

        /// <summary>
        /// Downloads for the Authentication Database.
        /// </summary>
        private static GithubResourceList AuthenticationDownloads { get; set; } = new GithubResourceList();
        
        /// <summary>
        /// Downloads for the Shard Database.
        /// </summary>
        private static GithubResourceList ShardDownloads { get; set; } = new GithubResourceList();
        
        /// <summary>
        /// Downloads for the World Database.
        /// </summary>
        private static GithubResourceList WorldDownloads { get; set; } = new GithubResourceList();

        public static void Initialize()
        {
            CurrentDb = new Database();
            CurrentDb.Initialize(ConfigManager.Config.MySql.World.Host,
                          ConfigManager.Config.MySql.World.Port,
                          ConfigManager.Config.MySql.World.Username,
                          ConfigManager.Config.MySql.World.Password,
                          ConfigManager.Config.MySql.World.Database, 
                          false);
            AuthenticationDownloads.DefaultDatabaseName = DefaultAuthenticationDatabaseName;
            AuthenticationDownloads.ConfigDatabaseName = ConfigManager.Config.MySql.Authentication.Database;
            ShardDownloads.DefaultDatabaseName = DefaultShardDatabaseName;
            ShardDownloads.ConfigDatabaseName = ConfigManager.Config.MySql.Shard.Database;
            WorldDownloads.DefaultDatabaseName = DefaultWorldDatabaseName;
            WorldDownloads.ConfigDatabaseName = ConfigManager.Config.MySql.World.Database;
        }

        /// <summary>
        /// Changes the database.
        /// </summary>
        private static void ResetDatabaseConnection(string newDatabase)
        {
            CurrentDb.ResetConnectionString(ConfigManager.Config.MySql.World.Host,
                          ConfigManager.Config.MySql.World.Port,
                          ConfigManager.Config.MySql.World.Username,
                          ConfigManager.Config.MySql.World.Password, 
                          newDatabase, 
                          false);
        }

        /// <summary>
        /// Captures the Rate Limit Values from the Response Header
        /// </summary>
        private static void CaptureWebHeaderData(WebHeaderCollection headers)
        {
            if (headers?.Count > 0)
            {
                int tmpInt = 0;
                double rateLimitEpoch = 0;
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
                if (double.TryParse(headers.Get("X-RateLimit-Reset"), out rateLimitEpoch))
                {
                    ApiResetTime = ConvertFromUnixTimestamp(rateLimitEpoch);
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
                CaptureWebHeaderData(w.ResponseHeaders);
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
        /// Implement if needed: converts content from individual files when called from the Github API.
        /// </summary>
        // private static string readApiContent(string base64string)
        // {
        //    var bin = Convert.FromBase64String(base64string);
        //    return Encoding.UTF8.GetString(bin);
        // }

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
                    var apiRequestData = RetrieveWebString(currentUrl);
                    if (apiRequestData != null)
                    {
                        var repoFiles = JArray.Parse(apiRequestData);
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
                    } else
                    {
                        log.Debug($"Issue found calling API.");
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

        private static Tuple<string, GithubResourceType> GetDatabaseNameAndResourceType(string searchString, string fileName)
        {
            var localType = GithubResourceType.Unknown;
            var databaseName = string.Empty;
            if (searchString.Contains(".txt"))
            {
                localType = GithubResourceType.TextFile;
            }
            else if (searchString.Contains("/Base"))
            {
                localType = GithubResourceType.SqlBaseFile;
                if (fileName.Contains("AuthenticationBase"))
                {
                    databaseName = DefaultAuthenticationDatabaseName;
                }
                else if (fileName.Contains("ShardBase"))
                {
                    databaseName = DefaultShardDatabaseName;
                }
                else if (fileName.Contains("WorldBase"))
                {
                    databaseName = DefaultWorldDatabaseName;
                }
            }
            else if (searchString.Contains("/Updates"))
            {
                localType = GithubResourceType.SqlUpdateFile;
                if (searchString.Contains("/Authentication"))
                {
                    databaseName = DefaultAuthenticationDatabaseName;
                }
                else if (searchString.Contains("/Shard"))
                {
                    databaseName = DefaultShardDatabaseName;
                }
                else if (searchString.Contains("/World"))
                {
                    databaseName = DefaultWorldDatabaseName;
                }
            } else if (searchString.Contains(".sql"))
            {
                localType = GithubResourceType.SqlFile;
            }
            return Tuple.Create<string, GithubResourceType>(databaseName, localType);
        }

        public static List<GithubResource> RetrieveGithubFolderList(string url)
        {
            // Check to see if the input is usable and the data path is valid
            if (url?.Length > 0)
            {
                List<GithubResource> downloadList = new List<GithubResource>();
                var localDataPath = Path.GetFullPath(ConfigManager.Config.ContentServer.LocalDataPath);
                List<string> directoryUrls = new List<string>();
                directoryUrls.Add(url);
                // Recurse api and collect all downloads
                while (directoryUrls.Count > 0)
                {
                    var currentUrl = directoryUrls.LastOrDefault();
                    var content = RetrieveWebString(currentUrl);
                    var repoFiles = content != null ? JArray.Parse(content) : null;
                    if (repoFiles?.Count > 0)
                    {
                        foreach (var file in repoFiles)
                        {
                            var search = file["path"].ToString();
                            if (search.Contains("Database"))
                            {
                                if (file["type"].ToString() == "dir")
                                {
                                    directoryUrls.Add(file["url"].ToString());
                                    CheckLocalDataPath(Path.Combine(localDataPath, file["path"].ToString()));
                                }
                                else
                                {
                                    var fileName = file["name"].ToString();
                                    var info = GetDatabaseNameAndResourceType(search, fileName);
                                    var databaseName = info.Item1;
                                    var localType = info.Item2;
                                    downloadList.Add(new GithubResource()
                                    {
                                        DatabaseName = databaseName,
                                        Type = localType,
                                        SourceUri = file["download_url"].ToString(),
                                        SourcePath = file["path"].ToString(),
                                        FileName = file["name"].ToString(),
                                        FilePath = Path.GetFullPath(Path.Combine(localDataPath, file["path"].ToString())),
                                        FileSize = (int)file["size"],
                                        Hash = file["sha"].ToString()
                                    });
                                }
                            }
                        }
                    }
                    // Cancel because the string was caught in exception
                    if (repoFiles == null)
                    {
                        log.Debug($"No files found within {currentUrl}");
                        return null;
                    }
                    // Remove the parsed url
                    directoryUrls.Remove(currentUrl);
                }
                // Download the files from the downloads tuple
                foreach (var download in downloadList)
                {
                    // If we cannot Retrieve content, return false for failure
                    if (!RetrieveWebContent(download.SourceUri, download.FilePath))
                        log.Debug($"Trouble downloading {download.SourceUri} : {download.FilePath}");
                }
                return downloadList;
            }
            log.Debug($"Invalid Url provided, please check configuration.");
            return null;
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
                        // (string)json["name"] + (string)json["tag_name"] + (string)json["published_at"];
                        // Collect header info that tells how much retries and time left till reset.
                        CaptureWebHeaderData(w.ResponseHeaders);
                    }
                }
                catch (Exception error)
                {
                    log.Debug($"Trouble capturing metadata from the Github API. {error.ToString()}");
                    return error.Message;
                }

                var worldArchive = Path.GetFullPath(Path.Combine(ConfigManager.Config.ContentServer.LocalDataPath, WorldGithubFilename));

                if (RetrieveWebContent(WorldGithubDownload, worldArchive))
                {
                    // Extract & delete
                    var extractionError = ExtractZip(worldArchive, Path.GetFullPath(Path.Combine(ConfigManager.Config.ContentServer.LocalDataPath, "Database\\ACE-World\\")));
                    if (extractionError?.Length > 0)
                    {
                        log.Debug($"Could not extract {worldArchive} {extractionError}");
                        return $"Error Extracting {extractionError}";
                    }
                }
                return null;
            }
            // No more calls left, detail the time remaining
            var errorMessage = $"You have exhausted your Github API Limit limt per hour. Please wait till {ApiResetTime}";
            log.Error(errorMessage);
            return $"You have exhausted your Github API Limit limt per hour. Please wait till {ApiResetTime}";
        }

        private static void ResetDatabase(string databaseName)
        {
            ResetDatabaseConnection(string.Empty);
            // Delete Database, to clear everything including stored procs and views.
            var dropResult = CurrentDb.DropDatabase(databaseName);
            if (dropResult != null)
            {
                log.Debug($"Error dropping database: {dropResult}");
            }

            // Create Database
            var createResult = CurrentDb.CreateDatabase(databaseName);
            if (createResult != null)
            {
                log.Debug($"Error dropping database: {createResult}");
            }
        }

        private static void ParseDownloads(List<GithubResource> list)
        {
            foreach (var download in list)
            {
                if (download.DatabaseName == DefaultAuthenticationDatabaseName)
                {
                    AuthenticationDownloads.Downloads.Add(download);
                }
                else if (download.DatabaseName == DefaultShardDatabaseName)
                {
                    ShardDownloads.Downloads.Add(download);
                }
                else if (download.DatabaseName == DefaultWorldDatabaseName)
                {
                    WorldDownloads.Downloads.Add(download);
                }
            }
        }

        /// <summary>
        /// Attempts to Load all databases and data from the appropriate downloaded folder.
        /// </summary>
        /// <remarks>                        
        /// Load Data from 2 or 3 locations, in sequential order:
        ///    First Search Path: ${Downloads}\\Database\\Base\\
        ///    Second Search Path if updating world database: ACE-World\\${WorldGithubFilename}
        ///    Third Search Path: ${Downloads}\\Database\\Updates\\
        /// </remarks>
        public static string RedeployAllDatabases()
        {
            if (RedeploymentActive)
                return "There is already an active redeployment in progress...";

            log.Debug("A full database Redeployment has been initiated!");
            // Determine if the config settings appear valid:
            if (ConfigManager.Config.MySql.World.Database?.Length > 0 && ConfigManager.Config.ContentServer.LocalDataPath?.Length > 0)
            {
                // Check the data path and create if needed.
                var localDataPath = ConfigManager.Config.ContentServer.LocalDataPath;
                if (CheckLocalDataPath(localDataPath))
                {
                    RedeploymentActive = true;
                    // Setup the database requirements.
                    Initialize();
                    // Download the database files from Github:
                    log.Debug("Attempting download of all database files from Github Folder.");
                    var databaseFiles = RetrieveGithubFolderList(ConfigManager.Config.ContentServer.DatabaseUrl);
                    if (databaseFiles?.Count > 0)
                    {
                        log.Debug("Downloading ACE-World Archive.");
                        RetreieveWorldData();

                        List<GithubResourceList> resources = new List<GithubResourceList>();

                        ParseDownloads(databaseFiles);

                        resources.Add(AuthenticationDownloads);
                        resources.Add(ShardDownloads);
                        resources.Add(WorldDownloads);

                        foreach (var resource in resources)
                        {
                            if (resource.Downloads.Count == 0) continue;

                            var baseFile = string.Empty;
                            List<string> updates = new List<string>();
                            
                            // Seporate base files from updates
                            foreach (var download in resource.Downloads)
                            {
                                if (download.Type == GithubResourceType.SqlBaseFile)
                                {
                                    baseFile = download.FilePath;
                                }
                                if (download.Type == GithubResourceType.SqlUpdateFile)
                                {
                                    updates.Add(download.FilePath);
                                }
                            }

                            try
                            {
                                // Delete and receate the database
                                ResetDatabase(resource.ConfigDatabaseName);

                                // First sequence, load the world base
                                if (File.Exists(baseFile))
                                    ReadAndLoadScript(baseFile, resource.ConfigDatabaseName, resource.DefaultDatabaseName);
                                else
                                {
                                    var errorMessage = $"There was an error locating the base file {baseFile} for {resource.DefaultDatabaseName}!";
                                    log.Debug(errorMessage);
                                    Console.WriteLine(errorMessage);
                                }

                                // Second, if this is the world database, we will load ACE-World
                                if (resource.DefaultDatabaseName == DefaultWorldDatabaseName)
                                {
                                    // Grab the archive folder path
                                    var worldDataPath = Path.GetFullPath(Path.Combine(ConfigManager.Config.ContentServer.LocalDataPath, WorldDataPath));
                                    var files = from file in Directory.EnumerateFiles(worldDataPath) where !file.Contains(".txt") select new { File = file };
                                    if (files.Count() > 0)
                                    {
                                        // Load all SQL files within the ACE-World folder
                                        foreach (var file in files)
                                        {                                            
                                            ReadAndLoadScript(file.File, resource.ConfigDatabaseName, resource.DefaultDatabaseName);
                                        }
                                    }
                                }

                                // Last, run all updates
                                if (updates.Count() > 0)
                                {
                                    foreach (var file in updates)
                                    {
                                        ReadAndLoadScript(file, resource.ConfigDatabaseName, resource.DefaultDatabaseName);
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
                                RedeploymentActive = false;
                                return errorMessage;
                            }
                        }
                        RedeploymentActive = false;
                        // Success
                        return null;
                    }
                    RedeploymentActive = false;
                    var couldNotDownload = "Troubles downloading content, please wait one hour or investigate the API call errors.";
                    log.Debug(couldNotDownload);
                    return couldNotDownload;
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

        /// <summary>
        /// Attempts to Load all world data present from the appropriate downloaded folder, into a name database with the name collected from the config.
        /// </summary>
        /// <remarks>
        /// This function is overwhelmed with complexity, due too the fact that we are merging around 4 different folder structures and using a SaaS (github).
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
                    RedeploymentActive = true;
                    // Setup the database requirements.
                    Initialize();
                    // Download the database files from Github:
                    if (CheckLocalDataPath(Path.GetFullPath(Path.Combine(localDataPath, WoldGithubBaseSqlPath))))
                    {
                        log.Debug("Downloading World Base SQL.");
                        RetrieveWebContent(ConfigManager.Config.ContentServer.WorldBaseUrl, Path.GetFullPath(Path.Combine(localDataPath, WoldGithubBaseSqlPath, WoldGithubBaseSqlFile)));
                    }
                    log.Debug("Downloading World Update Folder.");
                    RetrieveGithubFolder(ConfigManager.Config.ContentServer.WorldUpdateUrl);
                    log.Debug("Downloading ACE-World Archive.");
                    RetreieveWorldData();

                    var databaseName = ConfigManager.Config.MySql.World.Database;

                    // Drop out of databases to perform delete and creation
                    ResetDatabaseConnection(string.Empty);

                    // Delete Database, to clear everything including stored procs and views.
                    var dropResult = CurrentDb.DropDatabase(databaseName);
                    if (dropResult != null)
                    {
                        log.Debug($"Error dropping database: {dropResult}");
                    }

                    // Create Database
                    var createResult = CurrentDb.CreateDatabase(databaseName);
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
                            ReadAndLoadWorldScript(worldBase, databaseName);
                        else
                        {
                            RedeploymentActive = false;
                            return "There was an error locating the WorldBase.sql file!";
                        }

                        // Second, find all of the sql files in directory, and load them
                        var files = from file in Directory.EnumerateFiles(worldDataPath) where !file.Contains(".txt") select new { File = file };
                        if (files.Count() > 0)
                        {
                            foreach (var file in files)
                            {
                                ReadAndLoadWorldScript(file.File, databaseName);
                            }
                        }

                        // Last, find all of the sql files in directory, and load them
                        files = from file in Directory.EnumerateFiles(worldUpdatePath) where !file.Contains(".txt") select new { File = file };
                        if (files.Count() > 0)
                        {
                            foreach (var file in files)
                            {
                                ReadAndLoadWorldScript(file.File, databaseName);
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
                        RedeploymentActive = false;
                        return errorMessage;
                    }
                    // Success
                    log.Debug("Finished redeployment!");
                    RedeploymentActive = false;
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

        private static string ReadAndLoadScript(string sqlFile, string databaseName, string defaultDatabase)
        {
            Console.Write(Environment.NewLine + $"{databaseName} Loading {sqlFile}!..");
            log.Debug($"Reading {sqlFile} and executing against {databaseName}");
            // open file into string
            string sqlInputFile = File.ReadAllText(sqlFile);
            if (databaseName != defaultDatabase)
            {
                sqlInputFile = sqlInputFile.Replace(defaultDatabase, databaseName);
            }
            ResetDatabaseConnection(databaseName);
            return CurrentDb.ExecuteSqlQueryOrScript(sqlInputFile, databaseName, true);
        }

        private static string ReadAndLoadWorldScript(string sqlFile, string databaseName)
        {
            Console.Write(Environment.NewLine + $"{databaseName} Loading {sqlFile}!..");
            log.Debug($"Reading {sqlFile} and executing against {databaseName}");
            // open file into string
            string sqlInputFile = File.ReadAllText(sqlFile);
            if (databaseName != DefaultWorldDatabaseName)
            {
                    sqlInputFile = sqlInputFile.Replace(DefaultWorldDatabaseName, databaseName);
            }
            ResetDatabaseConnection(databaseName);
            return CurrentDb.ExecuteSqlQueryOrScript(sqlInputFile, databaseName, true);
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
        /// Converts a double to epoch time. Used with Github.
        /// </summary>
        public static DateTime ConvertFromUnixTimestamp(double unixTimeStamp)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc).AddSeconds(unixTimeStamp).ToLocalTime();
        }
    }
}
