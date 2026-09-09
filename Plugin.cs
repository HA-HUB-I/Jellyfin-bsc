using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
        /// Fetches channels list using thread-safe caching with fallback protection.
        /// </summary>
        public async Task<List<BulsatcomChannel>> GetChannelsWithCacheAsync(ILogger logger, CancellationToken cancellationToken)
        {
            var config = Configuration;
            if (string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.Password))
            {
                throw new InvalidOperationException("Bulsatcom username or password not configured.");
            }

            var cacheDurationHours = config.ChannelCacheDurationHours > 0 ? config.ChannelCacheDurationHours : 4;
            var cacheDuration = TimeSpan.FromHours(cacheDurationHours);

            lock (_cacheLock)
            {
                if (_cachedChannels != null && _cachedChannels.Count > 0 && (DateTime.UtcNow - _lastCacheTime) < cacheDuration)
                {
                    logger.LogInformation("Using cached Bulsatcom channels list ({Count} channels, age: {Age:F0}s, TTL: {Ttl}h)",
                        _cachedChannels.Count, (DateTime.UtcNow - _lastCacheTime).TotalSeconds, cacheDuration.TotalHours);
                    return _cachedChannels;
                }
            }

            logger.LogInformation("Channel cache expired or empty. Fetching fresh channel list from Bulsatcom API.");
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

            List<BulsatcomChannel>? channels = null;
            try
            {
                channels = await apiClient.GetChannelsAsync(session, config.OsType, cancellationToken);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogWarning(ex, "Bulsatcom session expired or unauthorized. Re-authenticating.");
                session = await apiClient.LoginAsync(config.Username, config.Password, config.OsType, cancellationToken);
                channels = await apiClient.GetChannelsAsync(session, config.OsType, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to refresh channels from Bulsatcom API due to network or server error.");
                lock (_cacheLock)
                {
                    if (_cachedChannels != null && _cachedChannels.Count > 0)
                    {
                        logger.LogInformation("Preserving existing {Count} cached channels to protect ongoing streams and PVR playback.", _cachedChannels.Count);
                        return _cachedChannels;
                    }
                }
                throw;
            }

            if (channels != null && channels.Count > 0)
            {
                lock (_cacheLock)
                {
                    _cachedSession = session;
                    _cachedChannels = channels;
                    _lastCacheTime = DateTime.UtcNow;
                }
                return channels;
            }
            else
            {
                lock (_cacheLock)
                {
                    if (_cachedChannels != null && _cachedChannels.Count > 0)
                    {
                        logger.LogWarning("Bulsatcom API returned 0 channels. Preserving previous {Count} cached channels.", _cachedChannels.Count);
                        return _cachedChannels;
                    }
                }
                return channels ?? new List<BulsatcomChannel>();
            }
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
                
                var pluginInstance = Plugin.Instance;
                if (pluginInstance == null)
                {
                    _logger.LogError("Plugin instance is null");
                    return;
                }

                var config = pluginInstance.Configuration;
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
                var dataPath = pluginInstance.DataFolderPath;
                if (string.IsNullOrEmpty(dataPath))
                {
                    _logger.LogError("Plugin DataFolderPath is not available");
                    return;
                }
                _logger.LogInformation($"Files will be saved to: {dataPath}");
                
                if (!Directory.Exists(dataPath))
                {
                    Directory.CreateDirectory(dataPath);
                    _logger.LogInformation($"Created data directory: {dataPath}");
                }

                // Ensure channel logos are cached locally for persistent offline display
                var logosDir = Path.Combine(dataPath, "logos");
                if (!Directory.Exists(logosDir) || Directory.GetFiles(logosDir, "*.png").Length < 10)
                {
                    await EnsureLogosDownloadedAsync(logosDir, cancellationToken);
                }

                progress?.Report(40);

                // Fetch channels (uses caching internally)
                var channels = await pluginInstance.GetChannelsWithCacheAsync(_logger, cancellationToken);
                
                if (channels == null || channels.Count == 0)
                {
                    _logger.LogWarning("No channels retrieved from Bulsatcom API");
                    return;
                }

                _logger.LogInformation($"Retrieved {channels.Count} channels from Bulsatcom");

                progress?.Report(60);

                // Construct local stream base URL dynamically from Jellyfin network configuration.
                int port = 8096;
                string baseUrlPath = string.Empty;

                try
                {
                    var netConfig = _configManager.GetConfiguration("network");
                    if (netConfig != null)
                    {
                        var portProp = netConfig.GetType().GetProperty("InternalHttpPort");
                        if (portProp?.GetValue(netConfig) is int p && p > 0)
                        {
                            port = p;
                        }

                        var baseProp = netConfig.GetType().GetProperty("BaseUrl");
                        if (baseProp?.GetValue(netConfig) is string b)
                        {
                            baseUrlPath = b;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not resolve dynamic network config, falling back to default port 8096");
                }

                if (!string.IsNullOrEmpty(baseUrlPath) && !baseUrlPath.StartsWith("/"))
                {
                    baseUrlPath = "/" + baseUrlPath;
                }
                var baseUrl = $"http://127.0.0.1:{port}{baseUrlPath}";

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
                    var ratingAttr = channel.IsAdult ? " tvg-rating=\"18\" parental-rating=\"18\"" : " tvg-rating=\"0\" parental-rating=\"0\"";
                    var logoUrl = $"{baseUrl}/Plugins/Bulsatcom/Logos/{channel.EpgName}.png";
                    m3uContent.AppendLine($"#EXTINF:{channel.ChannelId} radio=\"{radioValue}\" group-title=\"{channel.Genre}\" tvg-id=\"{channel.EpgName}\" tvg-name=\"{channel.Title}\" tvg-chno=\"{channel.ChannelId}\" tvg-logo=\"{logoUrl}\"{ratingAttr},{channel.Title}");
                    
                    // Route streams through local redirect endpoint
                    var redirectUrl = $"{baseUrl}/Plugins/Bulsatcom/Stream/{channel.ChannelId}";
                    m3uContent.AppendLine(redirectUrl);
                }
                
                var newM3uContent = m3uContent.ToString();
                bool m3uChanged = true;
                if (File.Exists(m3uPath))
                {
                    var existingM3u = await File.ReadAllTextAsync(m3uPath, cancellationToken);
                    if (string.Equals(existingM3u, newM3uContent, StringComparison.Ordinal))
                    {
                        m3uChanged = false;
                        _logger.LogInformation("M3U playlist content is unchanged. Skipping file write to prevent Live TV tuner reload and PVR client reset.");
                    }
                }

                if (m3uChanged)
                {
                    await File.WriteAllTextAsync(m3uPath, newM3uContent, cancellationToken);
                    _logger.LogInformation($"Successfully updated M3U file with {channels.Count} channels: {m3uPath}");
                }
                
                progress?.Report(80);

                // Generate EPG XML file
                bool epgChanged = false;
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
                            new XElement("display-name", channel.Title ?? string.Empty),
                            new XElement("icon", new XAttribute("src", $"{baseUrl}/Plugins/Bulsatcom/Logos/{channel.EpgName}.png"))
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
                                            EnhanceProgramCategories(newProg);
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
                            var programTitle = channel.ProgramTitle;
                            if (string.IsNullOrWhiteSpace(programTitle)) continue;

                            var startFormatted = FormatXmltvDate(channel.EffectiveStart);
                            var stopFormatted = FormatXmltvDate(channel.EffectiveStop);

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
                                    programTitle
                                )
                            );

                            if (!string.IsNullOrWhiteSpace(channel.Genre))
                            {
                                progEl.Add(new XElement("category",
                                    new XAttribute("lang", "bg"),
                                    channel.Genre
                                ));
                            }

                            var desc = channel.EffectiveDescription;
                            if (!string.IsNullOrWhiteSpace(desc))
                            {
                                progEl.Add(new XElement("desc",
                                    new XAttribute("lang", "bg"),
                                    desc
                                ));
                            }

                            EnhanceProgramCategories(progEl);
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

                    string newEpgContent;
                    using (var ms = new MemoryStream())
                    {
                        using (var writer = System.Xml.XmlWriter.Create(ms, settings))
                        {
                            doc.Save(writer);
                        }
                        newEpgContent = Encoding.UTF8.GetString(ms.ToArray());
                    }

                    epgChanged = true;
                    if (File.Exists(epgPath))
                    {
                        var currentEpg = await File.ReadAllTextAsync(epgPath, cancellationToken);
                        if (string.Equals(currentEpg, newEpgContent, StringComparison.Ordinal))
                        {
                            epgChanged = false;
                            _logger.LogInformation("EPG XML content is identical to existing file. Skipping file write.");
                        }
                    }

                    if (epgChanged)
                    {
                        await File.WriteAllTextAsync(epgPath, newEpgContent, cancellationToken);
                        _logger.LogInformation("Successfully updated EPG file: {Path}", epgPath);

                        // Invalidate Jellyfin's internal XMLTV cache so it immediately reloads the new guide
                        try
                        {
                            var xmltvCacheDir = Path.Combine(_configManager.ApplicationPaths.CachePath, "xmltv");
                            if (Directory.Exists(xmltvCacheDir))
                            {
                                foreach (var cacheFile in Directory.GetFiles(xmltvCacheDir, "*.xml"))
                                {
                                    try
                                    {
                                        File.Delete(cacheFile);
                                        _logger.LogInformation("Invalidated cached XMLTV file: {Path}", cacheFile);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogDebug(ex, "Could not delete cached XMLTV file: {Path}", cacheFile);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Error clearing Jellyfin XMLTV cache directory");
                        }
                    }
                }

                progress?.Report(100);
                _logger.LogInformation($"Bulsatcom file generation task completed. Storage location: {dataPath}");

                // Trigger Refresh Guide task in Jellyfin ONLY if files actually changed
                if (m3uChanged || epgChanged)
                {
                    if (config.EnableAutoGuideRefresh)
                    {
                        try
                        {
                            var refreshTask = _taskManager.ScheduledTasks.FirstOrDefault(t => t.Name == "Refresh Guide");
                            if (refreshTask != null)
                            {
                                _logger.LogInformation("M3U or EPG data changed. Triggering Jellyfin 'Refresh Guide' scheduled task...");
                                _ = Task.Run(() => _taskManager.Execute(refreshTask, new TaskOptions()));
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
                }
                else
                {
                    _logger.LogInformation("M3U and EPG are unchanged. Skipping 'Refresh Guide' to ensure active streams and PVR clients are not interrupted.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Bulsatcom file generation");
                throw;
            }
        }

        private async Task EnsureLogosDownloadedAsync(string logosDir, CancellationToken cancellationToken)
        {
            try
            {
                Directory.CreateDirectory(logosDir);
                var existingLogos = Directory.GetFiles(logosDir, "*.png");
                if (existingLogos.Length >= 260)
                {
                    _logger.LogInformation("Channel logos already present in {Path} ({Count} logos). Skipping download.", logosDir, existingLogos.Length);
                    return;
                }

                _logger.LogInformation("Checking for local Bulsatcom logos package...");

                var versionStr = Plugin.Instance?.Version != null ? $"v{Plugin.Instance.Version}" : "latest";
                var zipUrl = $"https://github.com/HA-HUB-I/Jellyfin-bsc/releases/download/{versionStr}/bulsat_logos.zip";
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Jellyfin-Bulsatcom-Plugin");

                using var response = await httpClient.GetAsync(zipUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Logos package not downloaded (HTTP {Status}). Using locally present logos in {Path}", response.StatusCode, logosDir);
                    return;
                }

                var zipBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                using var ms = new MemoryStream(zipBytes);
                using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

                int extracted = 0;
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name) || !entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var destPath = Path.Combine(logosDir, entry.Name);
                    entry.ExtractToFile(destPath, overwrite: true);
                    extracted++;
                }

                _logger.LogInformation("Successfully extracted {Count} channel logos to {Path}", extracted, logosDir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not auto-download logos pack (using locally present logos).");
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

        private static void EnhanceProgramCategories(XElement progEl)
        {
            var categories = progEl.Elements("category").Select(c => c.Value).ToList();
            var title = progEl.Element("title")?.Value ?? string.Empty;
            var fullText = string.Join(" ", categories) + " " + title;

            bool hasCategory(string name) => categories.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));

            // Movie
            if ((fullText.IndexOf("филм", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fullText.IndexOf("movie", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fullText.IndexOf("кино", StringComparison.OrdinalIgnoreCase) >= 0) && !hasCategory("Movie"))
            {
                progEl.Add(new XElement("category", new XAttribute("lang", "en"), "Movie"));
            }

            // Series
            if ((fullText.IndexOf("сериал", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fullText.IndexOf("series", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fullText.IndexOf("серии", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fullText.IndexOf("епизод", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fullText.IndexOf("сезон", StringComparison.OrdinalIgnoreCase) >= 0) && !hasCategory("Series"))
            {
                progEl.Add(new XElement("category", new XAttribute("lang", "en"), "Series"));
                if (progEl.Element("episode-num") == null)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(title, @"([Сс]езон\s*\d+)?\s*,?\s*([Ее]п\.\s*\d+)");
                    var epText = m.Success ? m.Value.Trim() : "Епизод";
                    progEl.Add(new XElement("episode-num", new XAttribute("system", "onscreen"), epText));
                }
            }

            // Sports
            if ((fullText.IndexOf("спорт", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fullText.IndexOf("sport", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fullText.IndexOf("мач", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fullText.IndexOf("футбол", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fullText.IndexOf("formula", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fullText.IndexOf("лига", StringComparison.OrdinalIgnoreCase) >= 0) && !hasCategory("Sports"))
            {
                progEl.Add(new XElement("category", new XAttribute("lang", "en"), "Sports"));
            }

            // News
            if ((fullText.IndexOf("новини", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fullText.IndexOf("news", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fullText.IndexOf("емисия", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fullText.IndexOf("информацион", StringComparison.OrdinalIgnoreCase) >= 0) && !hasCategory("News"))
            {
                progEl.Add(new XElement("category", new XAttribute("lang", "en"), "News"));
            }

            // Kids
            if ((fullText.IndexOf("детско", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fullText.IndexOf("kids", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fullText.IndexOf("анимаци", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fullText.IndexOf("children", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fullText.IndexOf("cartoon", StringComparison.OrdinalIgnoreCase) >= 0) && !hasCategory("Kids"))
            {
                progEl.Add(new XElement("category", new XAttribute("lang", "en"), "Kids"));
            }

            // Documentary
            if ((fullText.IndexOf("научно", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fullText.IndexOf("документ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fullText.IndexOf("documentary", StringComparison.OrdinalIgnoreCase) >= 0) && !hasCategory("Documentary"))
            {
                progEl.Add(new XElement("category", new XAttribute("lang", "en"), "Documentary"));
            }

            // Music
            if ((fullText.IndexOf("музик", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fullText.IndexOf("music", StringComparison.OrdinalIgnoreCase) >= 0) && !hasCategory("Music"))
            {
                progEl.Add(new XElement("category", new XAttribute("lang", "en"), "Music"));
            }
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // Run every 12 hours by default
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.IntervalTrigger,
                    IntervalTicks = TimeSpan.FromHours(12).Ticks
                }
            };
        }
    }
}