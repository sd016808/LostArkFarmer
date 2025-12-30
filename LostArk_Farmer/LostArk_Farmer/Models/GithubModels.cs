using Newtonsoft.Json;
using System.Collections.Generic;

namespace LostArkAutoPlayer.Models
{
    public class GithubRelease
    {
        [JsonProperty("tag_name")]
        public string TagName { get; set; }

        [JsonProperty("body")]
        public string Body { get; set; }

        [JsonProperty("assets")]
        public List<GithubAsset> Assets { get; set; }
    }

    public class GithubAsset
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("browser_download_url")]
        public string BrowserDownloadUrl { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }
    }
}