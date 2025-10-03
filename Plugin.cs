using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public override string Name => "Bulsatcom File Generator";
        public override Guid Id => Guid.Parse("f996e2e1-3335-4b39-adf2-417d38b18b6d");
        public override string Description => "Generates M3U and EPG files from Bulsatcom IPTV service";

        public static Plugin? Instance { get; private set; }

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
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
                    Name = "Settings",
                    EmbeddedResourcePath = "Jellyfin.Plugin.BulsatcomChannel.Configuration.basic-config.html"
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
                if (!Directory.Exists(dataPath))
                {
                    Directory.CreateDirectory(dataPath);
                    _logger.LogInformation($"Created data directory: {dataPath}");
                }

                progress?.Report(50);

                // Generate basic M3U file
                var m3uPath = Path.Combine(dataPath, config.M3uFileName);
                var basicM3u = "#EXTM3U\n#EXTINF:-1,Test Channel\nhttp://example.com/stream\n";
                
                await File.WriteAllTextAsync(m3uPath, basicM3u, cancellationToken);
                _logger.LogInformation($"Generated basic M3U file: {m3uPath}");

                progress?.Report(100);
                _logger.LogInformation("Bulsatcom file generation completed successfully");
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