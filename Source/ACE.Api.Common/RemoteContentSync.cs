using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ACE.Api.Common
{
    public static class RemoteContentSync
    {
        /// <summary>
        /// Retreieves a string from a web location.
        /// </summary>
        public static string GetWebString(string updateUrl)
        {
                using (WebClient webClient = new WebClient())
                {
                    WebClient w = new WebClient();
                    // Header is required for github
                    w.Headers.Add("User-Agent", "ACEmulator.Api");
                    return (w.DownloadString(updateUrl));
                }
            }
        }

        /// <summary>
        /// Retreives a file from a web location.
        /// </summary>
        public static bool GetWebContent(string url, string destinationFilePath)
        {
        return true    
        }
    }
}
