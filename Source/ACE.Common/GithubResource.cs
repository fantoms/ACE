﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACE.Common
{
    /// <summary>
    /// Github Data resource for managing files and folders in the github api.
    /// </summary>
    public class GithubResource
    {
        public string DatabaseName { get; set; }
        public List<GithubResourceData> Downloads { get; set; }
    }
}