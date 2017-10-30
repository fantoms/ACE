using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACE.Api.Models
{
    public class RedeployRequest
    {
        [DefaultValue(false)]
        [JsonProperty("force")]
        public bool ForceDeploy { get; set; } = false;
    }
}
