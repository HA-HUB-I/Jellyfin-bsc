using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.BulsatcomChannel.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string ApiUrl { get; set; } = "https://api.iptv.bulsat.com";
        public int Timeout { get; set; } = 30;
        public int OsType { get; set; } = 1; // 0 for pcweb, 1 for samsungtv
        public bool DownloadEpg { get; set; } = true;
        public bool Debug { get; set; } = false;
        public string BlockedGenres { get; set; } = "";
        
        // Refresh settings
        public int RefreshIntervalHours { get; set; } = 6;
        public int MinRefreshIntervalHours { get; set; } = 1;
        public int MaxRefreshIntervalHours { get; set; } = 24;
        
        // File settings
        public string M3uFileName { get; set; } = "bulsat.m3u";
        public string EpgFileName { get; set; } = "bulsat.xml";
        
        // Connection settings
        public int MaxRetries { get; set; } = 3;
        public bool ValidateCredentials { get; set; } = true;
        
        // Last operation status
        public string LastError { get; set; } = "";
        public DateTime LastSuccessfulUpdate { get; set; } = DateTime.MinValue;
        public int TotalChannels { get; set; } = 0;
        
        // Scheduled task settings
        public bool EnableScheduledTask { get; set; } = true;
        public int UpdateIntervalHours { get; set; } = 6;
    }
}
