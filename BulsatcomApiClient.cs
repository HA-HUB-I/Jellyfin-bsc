using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BulsatcomChannel
{
    /// <summary>
    /// API client for Bulsatcom IPTV service
    /// </summary>
    public class BulsatcomApiClient
    {
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        private const string ApiUrl = "https://api.iptv.bulsat.com";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/61.0.3163.100 Safari/537.36";
        
        private readonly string[] _osTypes = { "pcweb", "samsungtv" };
        
        public BulsatcomApiClient(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            
            _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            _httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "bg-BG,bg;q=0.8,en;q=0.6");
            _httpClient.DefaultRequestHeaders.Add("Origin", "https://test.iptv.bulsat.com");
            _httpClient.DefaultRequestHeaders.Add("Referer", "https://test.iptv.bulsat.com/iptv-login.php");
        }

        /// <summary>
        /// Authenticate with Bulsatcom API
        /// </summary>
        public async Task<string> LoginAsync(string username, string password, int osType, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation($"Attempting login for user: {username}");
                
                var authUrl = osType == 0 ? $"{ApiUrl}/auth" : $"{ApiUrl}/?auth";
                
                // First request to get challenge and session
                var response = await _httpClient.PostAsync(authUrl, null, cancellationToken);
                
                if (!response.Headers.TryGetValues("challenge", out var challengeValues) ||
                    !response.Headers.TryGetValues("ssbulsatapi", out var sessionValues))
                {
                    throw new Exception("Missing authentication headers");
                }

                var challenge = challengeValues.First();
                var session = sessionValues.First();
                
                _logger.LogDebug($"Got challenge: {challenge}, session: {session}");
                
                // Check if already logged in
                if (response.Headers.TryGetValues("logged", out var loggedValues) && 
                    loggedValues.First() == "true")
                {
                    _logger.LogInformation("Already logged in");
                    return session;
                }

                // Encrypt password with AES ECB
                var encryptedPassword = EncryptPassword(password, challenge);
                
                // Second request with credentials
                _httpClient.DefaultRequestHeaders.Remove("SSBULSATAPI");
                _httpClient.DefaultRequestHeaders.Add("SSBULSATAPI", session);
                
                var formData = new Dictionary<string, string>
                {
                    { "user", username },
                    { "device_id", _osTypes[osType] },
                    { "device_name", _osTypes[osType] },
                    { "os_version", _osTypes[osType] },
                    { "os_type", _osTypes[osType] },
                    { "app_version", "0.01" },
                    { "pass", encryptedPassword }
                };

                var content = new FormUrlEncodedContent(formData);
                response = await _httpClient.PostAsync(authUrl, content, cancellationToken);

                if (!response.Headers.TryGetValues("logged", out var loginResult) || 
                    loginResult.First() != "true")
                {
                    _logger.LogError($"Login failed for user: {username}");
                    throw new UnauthorizedAccessException("Invalid Bulsatcom credentials");
                }

                _logger.LogInformation($"Successfully logged in as: {username}");
                return session;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Login error for user {username}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Get list of live TV channels
        /// </summary>
        public async Task<List<BulsatcomChannel>> GetChannelsAsync(string session, int osType, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Fetching channels list");
                
                var channelsUrl = $"{ApiUrl}/tv/{_osTypes[osType]}/live";
                
                _httpClient.DefaultRequestHeaders.Remove("SSBULSATAPI");
                _httpClient.DefaultRequestHeaders.Add("SSBULSATAPI", session);
                _httpClient.DefaultRequestHeaders.Remove("Access-Control-Request-Method");
                _httpClient.DefaultRequestHeaders.Add("Access-Control-Request-Method", "POST");
                _httpClient.DefaultRequestHeaders.Remove("Access-Control-Request-Headers");
                _httpClient.DefaultRequestHeaders.Add("Access-Control-Request-Headers", "ssbulsatapi");
                
                // OPTIONS request
                await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Options, channelsUrl), cancellationToken);
                
                // POST request
                var response = await _httpClient.PostAsync(channelsUrl, null, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"Failed to get channels: {response.StatusCode}");
                    return new List<BulsatcomChannel>();
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var channels = JsonSerializer.Deserialize<List<BulsatcomChannel>>(json) ?? new List<BulsatcomChannel>();
                
                _logger.LogInformation($"Successfully fetched {channels.Count} channels");
                return channels;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting channels: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Encrypt password using AES ECB mode
        /// </summary>
        private string EncryptPassword(string password, string key)
        {
            // Pad password to 16 bytes
            var paddedPassword = password + new string('\0', 16 - (password.Length % 16));
            var passwordBytes = Encoding.UTF8.GetBytes(paddedPassword);
            var keyBytes = Encoding.UTF8.GetBytes(key);

            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.Key = keyBytes;

                using (var encryptor = aes.CreateEncryptor())
                {
                    var encrypted = encryptor.TransformFinalBlock(passwordBytes, 0, passwordBytes.Length);
                    return Convert.ToBase64String(encrypted);
                }
            }
        }
    }

    /// <summary>
    /// Represents a Bulsatcom TV channel
    /// </summary>
    public class BulsatcomChannel
    {
        [JsonPropertyName("channel")]
        public string? ChannelId { get; set; }
        
        [JsonPropertyName("title")]
        public string? Title { get; set; }
        
        [JsonPropertyName("epg_name")]
        public string? EpgName { get; set; }
        
        [JsonPropertyName("sources")]
        public string? Sources { get; set; }
        
        [JsonPropertyName("radio")]
        public string? Radio { get; set; }
        
        [JsonPropertyName("genre")]
        public string? Genre { get; set; }
    }
}
