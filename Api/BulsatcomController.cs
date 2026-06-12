using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BulsatcomChannel.Api
{
    /// <summary>
    /// API controller for Bulsatcom channel streaming redirects
    /// </summary>
    [ApiController]
    [Route("Plugins/Bulsatcom")]
    public class BulsatcomController : ControllerBase
    {
        private readonly ILogger<BulsatcomController> _logger;

        public BulsatcomController(ILogger<BulsatcomController> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Redirects to the active Bulsatcom stream URL for the given channel ID
        /// </summary>
        [HttpGet("Stream/{channelId}")]
        public async Task<IActionResult> GetStream(string channelId)
        {
            _logger.LogInformation("Streaming request received for Bulsatcom channel ID: {ChannelId}", channelId);

            if (Plugin.Instance == null)
            {
                _logger.LogError("Bulsatcom plugin instance is null");
                return StatusCode(500, "Plugin not initialized");
            }

            try
            {
                var streamUrl = await Plugin.Instance.GetStreamUrlAsync(channelId, _logger, HttpContext.RequestAborted);
                
                if (string.IsNullOrEmpty(streamUrl))
                {
                    _logger.LogWarning("Stream URL not found for channel ID: {ChannelId}", channelId);
                    return NotFound($"Channel {channelId} stream not found");
                }

                _logger.LogDebug("Redirecting channel {ChannelId} to: {StreamUrl}", channelId, streamUrl);
                return Redirect(streamUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while redirecting channel ID: {ChannelId}", channelId);
                return StatusCode(500, ex.Message);
            }
        }
    }
}
