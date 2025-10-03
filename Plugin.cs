using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.BulsatcomChannel.Configuration;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BulsatcomChannel
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public override string Name => "Bulsatcom File Generator";
        public override Guid Id => Guid.Parse("f996e2e1-3335-4b39-adf2-417d38b18b6d");

        public static Plugin Instance { get; private set; }

        public Plugin(ILogger<Plugin> logger)
            : base(logger)
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
        public bool IsHidden => false;
        public bool IsEnabled => true;

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.LogInformation("Starting Bulsatcom file generation.");

            try
            {
                var dataPath = Plugin.Instance.DataFolderPath;
                _logger.LogInformation($"Plugin data path: {dataPath}");

                if (!Directory.Exists(dataPath))
                {
                    Directory.CreateDirectory(dataPath);
                }

                var config = Plugin.Instance.Configuration;

                var session = await Login(config.Username, config.Password, config.ApiUrl, config.OsType, cancellationToken);
                _logger.LogInformation("Successfully logged in.");
                progress.Report(20.0);

                // TODO: Continue translation...

                progress.Report(100.0);
                _logger.LogInformation("Bulsatcom file generation completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Bulsatcom file generation.");
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

    // Classes for JSON deserialization
    public class BulsatcomChannel
    {
        [JsonPropertyName("epg_name")]
        public string EpgName { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("sources")]
        public string Sources { get; set; }

        [JsonPropertyName("radio")]
        public string Radio { get; set; }

        [JsonPropertyName("genre")]
        public string Genre { get; set; }

        [JsonPropertyName("channel")]
        public string ChannelId { get; set; }

        [JsonPropertyName("program")]
        public BulsatcomProgram Program { get; set; }
    }

    public class BulsatcomProgram
    {
        // TODO: Define program properties based on EPG JSON structure
    }
}