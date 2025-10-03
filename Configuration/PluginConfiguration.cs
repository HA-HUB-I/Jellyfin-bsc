using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.BulsatcomChannel.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string Username { get; set; } = "test";
        public string Password { get; set; } = "test";
        public string ApiUrl { get; set; } = "https://api.iptv.bulsat.com";
        public int Timeout { get; set; } = 10;
        public int OsType { get; set; } = 1; // 0 for pcweb, 1 for samsungtv
        public bool DownloadEpg { get; set; } = true;
        public bool Debug { get; set; } = false;
        public string BlockedGenres { get; set; } = "";
    }
}
