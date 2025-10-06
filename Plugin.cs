using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.BulsatcomChannel.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BulsatcomChannel
{
    /// <summary>
    /// Minimal Bulsatcom plugin for Jellyfin - focused on stability
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IDisposable
    {
        public override string Name => "Bulsatcom File Generator";
        public override Guid Id => Guid.Parse("f996e2e1-3335-4b39-adf2-417d38b18b6d");
        public override string Description => "Generates M3U and EPG files from Bulsatcom IPTV service";

        public static Plugin? Instance { get; private set; }
        private bool _disposed = false;

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        /// <summary>
        /// Cleanup resources when plugin is uninstalled or disabled
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Clean up managed resources
                    Instance = null;
                }
                _disposed = true;
            }
        }

        /// <summary>
        /// Public dispose method for IDisposable
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Gets the plugin configuration
        /// </summary>
        public PluginConfiguration PluginConfiguration => Configuration;

        /// <summary>
        /// Gets the configuration pages for the plugin
        /// </summary>
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    DisplayName = "Bulsatcom Channel",
                    Name = "BulsatcomConfigPage",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html",
                    EnableInMainMenu = true,
                    MenuSection = "server",
                    MenuIcon = "live_tv"
                },
                new PluginPageInfo
                {
                    Name = "BulsatcomConfigPageJs",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.js"
                }
            };
        }
    }

    /// <summary>
    /// Scheduled task for generating Bulsatcom files
    /// </summary>
    public class BulsatcomScheduledTask : IScheduledTask
    {
        private readonly ILogger<BulsatcomScheduledTask> _logger;

        public BulsatcomScheduledTask(ILogger<BulsatcomScheduledTask> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string Name => "Generate Bulsatcom Files";
        public string Description => "Generates M3U playlist and EPG files from Bulsatcom IPTV service";
        public string Category => "Live TV";
        public string Key => "BulsatcomFileGeneration";
        public bool IsHidden => false;
        public bool IsEnabled => true;

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting Bulsatcom file generation task");
            
            try
            {
                progress?.Report(0);
                
                var config = Plugin.Instance?.Configuration;
                if (config == null)
                {
                    _logger.LogError("Plugin configuration is null");
                    return;
                }

                // Basic validation
                if (string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.Password))
                {
                    _logger.LogWarning("Username or password not configured");
                    return;
                }

                progress?.Report(25);

                // Create output directory
                var dataPath = Plugin.Instance.DataFolderPath;
                _logger.LogInformation($"Files will be saved to: {dataPath}");
                
                if (!Directory.Exists(dataPath))
                {
                    Directory.CreateDirectory(dataPath);
                    _logger.LogInformation($"Created data directory: {dataPath}");
                }

                progress?.Report(40);

                // Authenticate with Bulsatcom API
                _logger.LogInformation($"Authenticating with Bulsatcom API for user: {config.Username}");
                
                var apiClient = new BulsatcomApiClient(_logger);
                string session;
                
                try
                {
                    session = await apiClient.LoginAsync(config.Username, config.Password, config.OsType, cancellationToken);
                }
                catch (UnauthorizedAccessException)
                {
                    _logger.LogError($"Authentication failed for user: {config.Username}. Please check your credentials.");
                    throw;
                }

                progress?.Report(60);

                // Get channels list
                var channels = await apiClient.GetChannelsAsync(session, config.OsType, cancellationToken);
                
                if (channels == null || channels.Count == 0)
                {
                    _logger.LogWarning("No channels retrieved from Bulsatcom API");
                    return;
                }

                _logger.LogInformation($"Retrieved {channels.Count} channels from Bulsatcom");

                progress?.Report(75);

                // Generate M3U file
                var m3uPath = Path.Combine(dataPath, config.M3uFileName);
                var m3uContent = new StringBuilder("#EXTM3U\n");
                
                foreach (var channel in channels)
                {
                    if (!string.IsNullOrWhiteSpace(config.BlockedGenres) && 
                        config.BlockedGenres.Split(',').Any(g => g.Trim() == channel.Genre))
                    {
                        continue;
                    }
                    
                    var radioValue = channel.Radio ? "true" : "false";
                    m3uContent.AppendLine($"#EXTINF:{channel.ChannelId} radio=\"{radioValue}\" group-title=\"{channel.Genre}\" tvg-logo=\"{channel.EpgName}.png\" tvg-id=\"{channel.EpgName}\",{channel.Title}");
                    m3uContent.AppendLine(channel.Sources);
                }
                
                await File.WriteAllTextAsync(m3uPath, m3uContent.ToString(), cancellationToken);
                _logger.LogInformation($"Successfully generated M3U file with {channels.Count} channels: {m3uPath}");
                
                progress?.Report(90);

                // Generate basic EPG file (full EPG implementation can be added later)
                if (config.DownloadEpg)
                {
                    var epgPath = Path.Combine(dataPath, config.EpgFileName);
                    var basicEpg = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<tv>\n";
                    
                    foreach (var channel in channels)
                    {
                        if (!string.IsNullOrWhiteSpace(config.BlockedGenres) && 
                            config.BlockedGenres.Split(',').Any(g => g.Trim() == channel.Genre))
                        {
                            continue;
                        }
                        
                        basicEpg += $"  <channel id=\"{channel.EpgName}\">\n";
                        basicEpg += $"    <display-name>{channel.Title}</display-name>\n";
                        basicEpg += $"  </channel>\n";
                    }
                    
                    basicEpg += "</tv>\n";
                    
                    await File.WriteAllTextAsync(epgPath, basicEpg, cancellationToken);
                    _logger.LogInformation($"Successfully generated EPG file: {epgPath}");
                }

                progress?.Report(100);
                _logger.LogInformation($"Bulsatcom file generation completed successfully. Files saved to: {dataPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Bulsatcom file generation");
                throw;
            }
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // Run every 6 hours by default
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerInterval,
                    IntervalTicks = TimeSpan.FromHours(6).Ticks
                }
            };
        }
    }
}