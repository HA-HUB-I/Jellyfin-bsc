using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Jellyfin.Plugin.BulsatcomChannel.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BulsatcomChannel
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public override string Name => "Bulsatcom File Generator";
        public override Guid Id => Guid.Parse("f996e2e1-3335-4b39-adf2-417d38b18b6d");

        public static Plugin? Instance { get; private set; }

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config.html"
                }
            };
        }
    }

    public class BulsatcomScheduledTask : IScheduledTask
    {
        private readonly ILogger<BulsatcomScheduledTask> _logger;
        private readonly HttpClient _httpClient;

        public BulsatcomScheduledTask(ILogger<BulsatcomScheduledTask> logger)
        {
            _logger = logger;
            _httpClient = new HttpClient();
        }

        public string Name => "Generate Bulsatcom Files";
        public string Description => "Generates M3U and EPG files from Bulsatcom.";
        public string Category => "Live TV";
        public string Key => "BulsatcomFileGeneration";
        public bool IsHidden => false;
        public bool IsEnabled => true;

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting Bulsatcom file generation.");
            var config = Plugin.Instance?.Configuration;
            
            if (config == null)
            {
                _logger.LogError("Plugin configuration is null.");
                return;
            }

            try
            {
                // Clear previous error
                config.LastError = "";
                
                // Validate configuration
                if (string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.Password))
                {
                    var error = "Username and password are required. Please configure the plugin settings.";
                    _logger.LogError(error);
                    config.LastError = error;
                    Plugin.Instance?.SaveConfiguration();
                    return;
                }

                var dataPath = Plugin.Instance.DataFolderPath;
                _logger.LogInformation($"Plugin data path: {dataPath}");

                if (!Directory.Exists(dataPath))
                {
                    Directory.CreateDirectory(dataPath);
                }

                // Set HTTP client timeout
                _httpClient.Timeout = TimeSpan.FromSeconds(config.Timeout);
                
                progress.Report(10.0);

                // Login with retry logic
                string session = null;
                var retryCount = 0;
                var maxRetries = config.MaxRetries;
                
                while (retryCount < maxRetries && session == null)
                {
                    try
                    {
                        session = await Login(config.Username, config.Password, config.ApiUrl, config.OsType, cancellationToken);
                        if (!string.IsNullOrEmpty(session))
                        {
                            _logger.LogInformation("Successfully logged in.");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        _logger.LogWarning($"Login attempt {retryCount} failed: {ex.Message}");
                        
                        if (retryCount < maxRetries)
                        {
                            await Task.Delay(2000 * retryCount, cancellationToken); // Exponential backoff
                        }
                        else
                        {
                            throw new Exception($"Failed to login after {maxRetries} attempts: {ex.Message}", ex);
                        }
                    }
                }

                if (string.IsNullOrEmpty(session))
                {
                    throw new Exception("Failed to obtain session token. Please check your credentials.");
                }

                progress.Report(30.0);

                // Get channel list
                var channels = await GetChannelList(config.ApiUrl, cancellationToken);
                _logger.LogInformation($"Retrieved {channels.Count} channels.");
                progress.Report(50.0);

                // Download EPG if enabled
                if (config.DownloadEpg)
                {
                    await GetEpg(channels, config.ApiUrl, cancellationToken);
                    _logger.LogInformation("EPG data downloaded.");
                }
                progress.Report(70.0);

                // Generate M3U file
                var m3uContent = GenerateM3u(channels, config.BlockedGenres);
                var m3uPath = Path.Combine(dataPath, config.M3uFileName);
                await File.WriteAllTextAsync(m3uPath, m3uContent, cancellationToken);
                _logger.LogInformation($"M3U file saved to: {m3uPath}");
                progress.Report(85.0);

                // Generate XML TV file if EPG is enabled
                if (config.DownloadEpg)
                {
                    var xmlContent = GenerateXmlTv(channels, config.BlockedGenres);
                    var xmlPath = Path.Combine(dataPath, config.EpgFileName);
                    await File.WriteAllTextAsync(xmlPath, xmlContent, cancellationToken);
                    _logger.LogInformation($"EPG XML file saved to: {xmlPath}");
                }

                // Update configuration with success info
                config.LastSuccessfulUpdate = DateTime.UtcNow;
                config.TotalChannels = channels.Count;
                Plugin.Instance?.SaveConfiguration();

                progress.Report(100.0);
                _logger.LogInformation($"Bulsatcom file generation completed successfully. Generated {channels.Count} channels.");
            }
            catch (Exception ex)
            {
                var errorMessage = $"Error during Bulsatcom file generation: {ex.Message}";
                _logger.LogError(ex, errorMessage);
                
                // Save error to configuration
                if (config != null)
                {
                    config.LastError = errorMessage;
                    Plugin.Instance?.SaveConfiguration();
                }
                
                throw;
            }
        }

        private async Task<string> Login(string username, string password, string apiUrl, int osType, CancellationToken cancellationToken)
        {
            var osStrings = new[] { "pcweb", "samsungtv" };
            var osString = osStrings[osType];
            var authUrl = osType == 0 ? "/auth" : "/?auth";

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/61.0.3163.100 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Origin", "https://test.iptv.bulsat.com");
            _httpClient.DefaultRequestHeaders.Add("Referer", "https://test.iptv.bulsat.com/iptv-login.php");

            var response = await _httpClient.PostAsync(apiUrl + authUrl, null, cancellationToken);
            response.EnsureSuccessStatusCode();

            var challenge = response.Headers.GetValues("challenge").FirstOrDefault();
            var session = response.Headers.GetValues("ssbulsatapi").FirstOrDefault();

            if (response.Headers.TryGetValues("logged", out var loggedValues) && loggedValues.FirstOrDefault() == "true")
            {
                _httpClient.DefaultRequestHeaders.Add("SSBULSATAPI", session);
                return session;
            }

            _httpClient.DefaultRequestHeaders.Add("SSBULSATAPI", session);

            using var aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(challenge);
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;

            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var passwordPadded = new byte[passwordBytes.Length + (16 - passwordBytes.Length % 16)];
            Array.Copy(passwordBytes, passwordPadded, passwordBytes.Length);

            using var encryptor = aes.CreateEncryptor();
            var encryptedPasswordBytes = encryptor.TransformFinalBlock(passwordPadded, 0, passwordPadded.Length);
            var encryptedPasswordBase64 = Convert.ToBase64String(encryptedPasswordBytes);

            var loginData = new Dictionary<string, string>
            {
                { "user[]", username },
                { "device_id[]", osString },
                { "device_name[]", osString },
                { "os_version[]", osString },
                { "os_type[]", osString },
                { "app_version[]", "0.01" },
                { "pass[]", encryptedPasswordBase64 }
            };

            var loginResponse = await _httpClient.PostAsync(apiUrl + authUrl, new FormUrlEncodedContent(loginData), cancellationToken);
            loginResponse.EnsureSuccessStatusCode();

            if (loginResponse.Headers.TryGetValues("logged", out var loggedInValues) && loggedInValues.FirstOrDefault() == "true")
            {
                return session;
            }

            return session ?? string.Empty;
        }

        private async Task<List<BulsatcomChannel>> GetChannelList(string apiUrl, CancellationToken cancellationToken)
        {
            var channels = new List<BulsatcomChannel>();
            
            try
            {
                var channelsUrl = apiUrl + "/tv/channels";
                var response = await _httpClient.GetAsync(channelsUrl, cancellationToken);
                response.EnsureSuccessStatusCode();
                
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var channelData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                
                if (channelData != null)
                {
                    foreach (var kvp in channelData)
                    {
                        try
                        {
                            var channel = JsonSerializer.Deserialize<BulsatcomChannel>(kvp.Value.GetRawText());
                            if (channel != null)
                            {
                                channels.Add(channel);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Failed to deserialize channel {kvp.Key}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get channel list");
                throw;
            }
            
            return channels;
        }

        private string GenerateM3u(List<BulsatcomChannel> channels, string blockedGenres)
        {
            var blocked = new HashSet<string>(blockedGenres?.Split(',', StringSplitOptions.RemoveEmptyEntries) 
                .Select(g => g.Trim()) ?? Array.Empty<string>());
            
            var m3u = new StringBuilder();
            m3u.AppendLine("#EXTM3U");
            
            foreach (var channel in channels)
            {
                if (!string.IsNullOrEmpty(channel.Genre) && blocked.Contains(channel.Genre))
                {
                    continue;
                }
                
                if (!string.IsNullOrEmpty(channel.Sources) && !string.IsNullOrEmpty(channel.Title))
                {
                    var channelName = channel.Title;
                    var streamUrl = channel.Sources;
                    
                    m3u.AppendLine($"#EXTINF:-1 tvg-id=\"{channel.EpgName}\" tvg-name=\"{channelName}\" tvg-logo=\"\" group-title=\"{channel.Genre ?? "General"}\",{channelName}");
                    m3u.AppendLine(streamUrl);
                }
            }
            
            return m3u.ToString();
        }

        private async Task GetEpg(List<BulsatcomChannel> channels, string apiUrl, CancellationToken cancellationToken)
        {
            foreach (var channel in channels)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                var epgUrl = apiUrl + "/epg/short";
                var epgData = new Dictionary<string, string> { { "epg", "1day" }, { "channel", channel.EpgName } };

                var response = await _httpClient.PostAsync(epgUrl, new FormUrlEncodedContent(epgData), cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    var epgResponse = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                    if (epgResponse.TryGetValue(channel.EpgName, out var channelEpg) && channelEpg.TryGetProperty("programme", out var programme))
                    {
                        channel.Program = JsonSerializer.Deserialize<BulsatcomProgram>(programme.GetRawText());
                    }
                }
            }
        }

        private string GenerateXmlTv(List<BulsatcomChannel> channels, string blockedGenres)
        {
            var blocked = new HashSet<string>(blockedGenres?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>());
            var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null));
            var tv = new XElement("tv");

            foreach (var channel in channels)
            {
                if (!string.IsNullOrEmpty(channel.Genre) && blocked.Contains(channel.Genre))
                {
                    continue;
                }

                tv.Add(new XElement("channel", new XAttribute("id", channel.EpgName),
                    new XElement("display-name", new XAttribute("lang", "bg"), channel.Title)));

                if (channel.Program != null && !string.IsNullOrEmpty(channel.Program.Title))
                {
                    tv.Add(new XElement("programme",
                        new XAttribute("start", channel.Program.Start),
                        new XAttribute("stop", channel.Program.Stop),
                        new XAttribute("channel", channel.EpgName),
                        new XElement("title", new XAttribute("lang", ""), channel.Program.Title),
                        new XElement("desc", new XAttribute("lang", ""), channel.Program.Description),
                        new XElement("category", new XAttribute("lang", ""), channel.Genre)));
                }
            }

            doc.Add(tv);
            return doc.ToString();
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            var config = Plugin.Instance?.Configuration;
            var intervalHours = config?.RefreshIntervalHours ?? 6;
            
            // Ensure interval is within bounds
            intervalHours = Math.Max(config?.MinRefreshIntervalHours ?? 1, 
                           Math.Min(config?.MaxRefreshIntervalHours ?? 24, intervalHours));
            
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerInterval,
                    IntervalTicks = TimeSpan.FromHours(intervalHours).Ticks
                }
            };
        }
    }

    // Classes for JSON deserialization
    public class BulsatcomChannel
    {
        [JsonPropertyName("epg_name")]
        public string? EpgName { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("sources")]
        public string? Sources { get; set; }

        [JsonPropertyName("radio")]
        public string? Radio { get; set; }

        [JsonPropertyName("genre")]
        public string? Genre { get; set; }

        [JsonPropertyName("channel")]
        public string? ChannelId { get; set; }

        [JsonPropertyName("program")]
        public BulsatcomProgram? Program { get; set; }
    }

    public class BulsatcomProgram
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("start")]
        public string? Start { get; set; }

        [JsonPropertyName("stop")]
        public string? Stop { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}