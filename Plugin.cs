using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Jellyfin.Plugin.BulsatcomChannel.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Configuration;
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

        private readonly object _cacheLock = new object();
        private string? _cachedSession;
        private List<BulsatcomChannel>? _cachedChannels;
        private DateTime _lastCacheTime = DateTime.MinValue;

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        /// <summary>
        /// Gets the active Bulsatcom stream URL, using cache and refreshing session if needed.
        /// </summary>
        public async Task<string?> GetStreamUrlAsync(string channelId, ILogger logger, CancellationToken cancellationToken)
        {
            var channels = await GetChannelsWithCacheAsync(logger, cancellationToken);
            var channel = channels.FirstOrDefault(c => c.ChannelId == channelId);
            return channel?.Sources;
        }

        /// <summary>
        /// Fetches channels list using thread-safe caching.
        /// </summary>
        public async Task<List<BulsatcomChannel>> GetChannelsWithCacheAsync(ILogger logger, CancellationToken cancellationToken)
        {
            var config = Configuration;
            if (string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.Password))
            {
                throw new InvalidOperationException("Bulsatcom username or password not configured.");
            }

            lock (_cacheLock)
            {
                if (_cachedChannels != null && _cachedChannels.Count > 0 && (DateTime.UtcNow - _lastCacheTime) < TimeSpan.FromMinutes(15))
                {
                    logger.LogInformation("Using cached Bulsatcom channels list (age: {Age}s)", (DateTime.UtcNow - _lastCacheTime).TotalSeconds);
                    return _cachedChannels;
                }
            }

            logger.LogInformation("Cache expired or empty. Fetching fresh channel list from Bulsatcom API.");
            var apiClient = new BulsatcomApiClient(logger);
            
            string? session;
            lock (_cacheLock)
            {
                session = _cachedSession;
            }

            if (string.IsNullOrEmpty(session))
            {
                session = await apiClient.LoginAsync(config.Username, config.Password, config.OsType, cancellationToken);
            }

            List<BulsatcomChannel> channels;
            try
            {
                channels = await apiClient.GetChannelsAsync(session, config.OsType, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to get channels with cached session, trying login again.");
                session = await apiClient.LoginAsync(config.Username, config.Password, config.OsType, cancellationToken);
                channels = await apiClient.GetChannelsAsync(session, config.OsType, cancellationToken);
            }

            lock (_cacheLock)
            {
                _cachedSession = session;
                _cachedChannels = channels;
                _lastCacheTime = DateTime.UtcNow;
            }

            return channels;
        }

        /// <summary>
        /// Clears the cached session and channel list.
        /// </summary>
        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _cachedChannels = null;
                _cachedSession = null;
                _lastCacheTime = DateTime.MinValue;
            }
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
        private readonly IServerConfigurationManager _configManager;
        private readonly ITaskManager _taskManager;

        public BulsatcomScheduledTask(
            ILogger<BulsatcomScheduledTask> logger,
            IServerConfigurationManager configManager,
            ITaskManager taskManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            _taskManager = taskManager ?? throw new ArgumentNullException(nameof(taskManager));
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

                progress?.Report(20);

                // Create output directory
                var dataPath = Plugin.Instance.DataFolderPath;
                _logger.LogInformation($"Files will be saved to: {dataPath}");
                
                if (!Directory.Exists(dataPath))
                {
                    Directory.CreateDirectory(dataPath);
                    _logger.LogInformation($"Created data directory: {dataPath}");
                }

                progress?.Report(40);

                // Fetch channels (uses caching internally)
                var channels = await Plugin.Instance.GetChannelsWithCacheAsync(_logger, cancellationToken);
                
                if (channels == null || channels.Count == 0)
                {
                    _logger.LogWarning("No channels retrieved from Bulsatcom API");
                    return;
                }

                _logger.LogInformation($"Retrieved {channels.Count} channels from Bulsatcom");

                progress?.Report(60);

                // Read port and construct dynamic stream base URL
                var port = _configManager.Configuration.HttpServerPortNumber;
                var baseUrl = $"http://127.0.0.1:{port}";

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
                    
                    // Route streams through local redirect endpoint
                    var redirectUrl = $"{baseUrl}/Plugins/Bulsatcom/Stream/{channel.ChannelId}";
                    m3uContent.AppendLine(redirectUrl);
                }
                
                await File.WriteAllTextAsync(m3uPath, m3uContent.ToString(), cancellationToken);
                _logger.LogInformation($"Successfully generated M3U file with {channels.Count} channels: {m3uPath}");
                
                progress?.Report(80);

                // Generate EPG XML file
                if (config.DownloadEpg)
                {
                    var epgPath = Path.Combine(dataPath, config.EpgFileName);
                    var targetChannels = channels
                        .Where(c => string.IsNullOrWhiteSpace(config.BlockedGenres) || 
                                    !config.BlockedGenres.Split(',').Any(g => g.Trim() == c.Genre))
                        .ToList();

                    var doc = new XDocument(
                        new XDeclaration("1.0", "utf-8", "yes"),
                        new XElement("tv")
                    );
                    var tvElement = doc.Element("tv")!;

                    // Add channel elements
                    foreach (var channel in targetChannels)
                    {
                        var channelEl = new XElement("channel",
                            new XAttribute("id", channel.EpgName ?? string.Empty),
                            new XElement("display-name", channel.Title ?? string.Empty)
                        );
                        tvElement.Add(channelEl);
                    }

                    bool externalEpgLoaded = false;
                    var externalIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    // Try downloading and merging external EPG
                    if (!string.IsNullOrWhiteSpace(config.EpgSourceUrl))
                    {
                        try
                        {
                            _logger.LogInformation("Downloading EPG from external source: {Url}", config.EpgSourceUrl);
                            using (var httpClient = new HttpClient())
                            {
                                httpClient.Timeout = TimeSpan.FromSeconds(60);
                                var xmlBytes = await httpClient.GetByteArrayAsync(config.EpgSourceUrl, cancellationToken);
                                
                                string xmlContent;
                                if (xmlBytes.Length > 2 && xmlBytes[0] == 0x1F && xmlBytes[1] == 0x8B)
                                {
                                    _logger.LogInformation("Decompressing GZip external EPG data...");
                                    using (var ms = new MemoryStream(xmlBytes))
                                    using (var gzip = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Decompress))
                                    using (var reader = new StreamReader(gzip, Encoding.UTF8))
                                    {
                                        xmlContent = await reader.ReadToEndAsync(cancellationToken);
                                    }
                                }
                                else
                                {
                                    xmlContent = Encoding.UTF8.GetString(xmlBytes);
                                }

                                _logger.LogInformation("Parsing external XMLTV...");
                                var extDoc = XDocument.Parse(xmlContent);
                                var extTv = extDoc.Element("tv");
                                
                                if (extTv != null)
                                {
                                    // Map channel names to bulsat EpgNames
                                    var bulsatNormalMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                    foreach (var c in targetChannels)
                                    {
                                        if (!string.IsNullOrEmpty(c.Title))
                                            bulsatNormalMap[NormalizeName(c.Title)] = c.EpgName ?? string.Empty;
                                        if (!string.IsNullOrEmpty(c.EpgName))
                                            bulsatNormalMap[NormalizeName(c.EpgName)] = c.EpgName ?? string.Empty;
                                    }

                                    foreach (var chEl in extTv.Elements("channel"))
                                    {
                                        var extId = chEl.Attribute("id")?.Value;
                                        if (string.IsNullOrEmpty(extId)) continue;

                                        foreach (var nameEl in chEl.Elements("display-name"))
                                        {
                                            var normalized = NormalizeName(nameEl.Value);
                                            if (bulsatNormalMap.TryGetValue(normalized, out var bulsatEpgName))
                                            {
                                                externalIdMap[extId] = bulsatEpgName;
                                                break;
                                            }
                                        }

                                        if (!externalIdMap.ContainsKey(extId))
                                        {
                                            var normalizedId = NormalizeName(extId);
                                            if (bulsatNormalMap.TryGetValue(normalizedId, out var bulsatEpgName))
                                            {
                                                externalIdMap[extId] = bulsatEpgName;
                                            }
                                        }
                                    }

                                    _logger.LogInformation("Mapped {Count} channels to external EPG source", externalIdMap.Count);

                                    // Copy programmes
                                    int programmeCount = 0;
                                    foreach (var progEl in extTv.Elements("programme"))
                                    {
                                        var extChannelId = progEl.Attribute("channel")?.Value;
                                        if (extChannelId != null && externalIdMap.TryGetValue(extChannelId, out var bulsatEpgName))
                                        {
                                            var newProg = new XElement(progEl);
                                            newProg.SetAttributeValue("channel", bulsatEpgName);
                                            tvElement.Add(newProg);
                                            programmeCount++;
                                        }
                                    }

                                    _logger.LogInformation("Added {Count} programme entries from external EPG", programmeCount);
                                    externalEpgLoaded = true;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to load external EPG, falling back to basic API EPG.");
                        }
                    }

                    // Fallback to current show EPG from Bulsatcom API for unmapped/missing channels
                    var channelsWithoutPrograms = targetChannels
                        .Where(c => !externalEpgLoaded || !externalIdMap.ContainsValue(c.EpgName ?? string.Empty))
                        .ToList();

                    if (channelsWithoutPrograms.Count > 0)
                    {
                        _logger.LogInformation("Generating EPG from Bulsatcom API current program info for {Count} channels", channelsWithoutPrograms.Count);
                        int apiProgCount = 0;
                        foreach (var channel in channelsWithoutPrograms)
                        {
                            if (string.IsNullOrWhiteSpace(channel.Program)) continue;

                            var startFormatted = FormatXmltvDate(channel.Start);
                            var stopFormatted = FormatXmltvDate(channel.Stop);

                            if (string.IsNullOrEmpty(startFormatted))
                            {
                                startFormatted = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss +0000").Replace(":", "");
                            }
                            if (string.IsNullOrEmpty(stopFormatted))
                            {
                                var startDto = DateTimeOffset.UtcNow;
                                stopFormatted = startDto.AddHours(2).ToString("yyyyMMddHHmmss +0000").Replace(":", "");
                            }

                            var progEl = new XElement("programme",
                                new XAttribute("start", startFormatted),
                                new XAttribute("stop", stopFormatted),
                                new XAttribute("channel", channel.EpgName ?? string.Empty),
                                new XElement("title", 
                                    new XAttribute("lang", "bg"),
                                    channel.Program
                                )
                            );

                            if (!string.IsNullOrWhiteSpace(channel.Description))
                            {
                                progEl.Add(new XElement("desc",
                                    new XAttribute("lang", "bg"),
                                    channel.Description
                                ));
                            }

                            tvElement.Add(progEl);
                            apiProgCount++;
                        }
                        _logger.LogInformation("Added {Count} current program listings from Bulsatcom API", apiProgCount);
                    }

                    var settings = new System.Xml.XmlWriterSettings
                    {
                        Indent = true,
                        Encoding = Encoding.UTF8
                    };
                    using (var writer = System.Xml.XmlWriter.Create(epgPath, settings))
                    {
                        doc.Save(writer);
                    }
                    _logger.LogInformation("Successfully generated EPG file: {Path}", epgPath);
                }

                progress?.Report(100);
                _logger.LogInformation($"Bulsatcom file generation completed successfully. Files saved to: {dataPath}");

                // Trigger Refresh Guide task in Jellyfin programmatically
                try
                {
                    var refreshTask = _taskManager.ScheduledTasks.FirstOrDefault(t => t.Key == "RefreshGuide" || t.Name == "Refresh Guide");
                    if (refreshTask != null)
                    {
                        _logger.LogInformation("Triggering Jellyfin 'Refresh Guide' scheduled task...");
                        _taskManager.Execute(refreshTask, new TaskOptions());
                    }
                    else
                    {
                        _logger.LogWarning("Jellyfin 'Refresh Guide' scheduled task not found.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while triggering Jellyfin 'Refresh Guide' scheduled task.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Bulsatcom file generation");
                throw;
            }
        }

        private static string FormatXmltvDate(string? dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr))
            {
                return string.Empty;
            }

            if (long.TryParse(dateStr, out var unixTime))
            {
                try
                {
                    var dto = DateTimeOffset.FromUnixTimeSeconds(unixTime);
                    return dto.ToString("yyyyMMddHHmmss zzz").Replace(":", "");
                }
                catch
                {
                    // Ignore
                }
            }

            if (DateTime.TryParse(dateStr, out var dt))
            {
                var dto = new DateTimeOffset(dt);
                return dto.ToString("yyyyMMddHHmmss zzz").Replace(":", "");
            }

            return dateStr;
        }

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            
            var translit = TransliterateBulgarian(name);
            
            var sb = new StringBuilder();
            foreach (var c in translit.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
            }
            
            var normalized = sb.ToString();
            normalized = normalized
                .Replace("fhd", "")
                .Replace("hd", "")
                .Replace("sd", "")
                .Replace("tv", "")
                .Replace("тв", "")
                .Replace("bg", "")
                .Replace("бг", "");
                
            return normalized;
        }

        private static string TransliterateBulgarian(string text)
        {
            var map = new Dictionary<char, string>
            {
                {'а', "a"}, {'б', "b"}, {'в', "v"}, {'г', "g"}, {'д', "d"},
                {'е', "e"}, {'ж', "zh"}, {'з', "z"}, {'и', "i"}, {'й', "y"},
                {'к', "k"}, {'л', "l"}, {'м', "m"}, {'н', "n"}, {'о', "o"},
                {'п', "p"}, {'р', "r"}, {'с', "s"}, {'т', "t"}, {'у', "u"},
                {'ф', "f"}, {'х', "h"}, {'ц', "ts"}, {'ч', "ch"}, {'ш', "sh"},
                {'щ', "sht"}, {'ъ', "a"}, {'ь', "y"}, {'ю', "yu"}, {'я', "ya"}
            };
            
            var sb = new StringBuilder();
            foreach (var c in text.ToLowerInvariant())
            {
                if (map.TryGetValue(c, out var replacement))
                {
                    sb.Append(replacement);
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
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