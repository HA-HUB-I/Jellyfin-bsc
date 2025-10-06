# Jellyfin Bulsatcom File Generator Plugin

This plugin for Jellyfin periodically generates an M3U playlist and an XMLTV EPG file from the Bulsatcom IPTV service.

## How it works

This plugin runs as a scheduled task within Jellyfin. It logs into the Bulsatcom API, fetches the channel list and program guide, and saves the data as `bulsat.m3u` and `bulsat.xml` files in the plugin's data directory.

## Installation

### Option 1: From Jellyfin Plugin Repository (Recommended)

1. Open Jellyfin Admin Dashboard
2. Go to **Plugins → Repositories → Add Repository**
3. Enter:
   - **Repository Name:** `Bulsatcom Plugin`
   - **Repository URL:** `https://raw.githubusercontent.com/HA-HUB-I/Jellyfin-bsc/master/manifest.json`
4. Save and go to **Plugins → Catalog**
5. Find "Bulsatcom File Generator" and install it
6. Restart Jellyfin
7. Configure your username and password in plugin settings

### Option 2: Manual Installation

1. Download the latest `plugin.zip` from [Releases](https://github.com/HA-HUB-I/Jellyfin-bsc/releases)
2. Extract to your Jellyfin plugins directory
3. Restart Jellyfin
4. Configure your username and password in plugin settings

## Configuration

1. Go to **Plugins** in Jellyfin dashboard
2. Find "Bulsatcom File Generator" and click **Settings**
3. Enter your Bulsatcom credentials:
   - **Username:** Your Bulsatcom username
   - **Password:** Your Bulsatcom password
   - **API URL:** Default is correct for most users
   - **OS Type:** Choose between PC Web (0) or Samsung TV (1)

## Usage

1. Run the "Generate Bulsatcom Files" scheduled task manually or wait for automatic execution
2. Check Jellyfin logs to find the path to generated files:
   - `bulsat.m3u` - Channel playlist
   - `bulsat.xml` - EPG data
3. Configure your Live TV settings:
   - **M3U Tuner:** Point to the generated `.m3u` file path
   - **XMLTV EPG:** Point to the generated `.xml` file path

## Uninstalling / Disabling

### To Disable the Plugin:
1. Go to **Dashboard → Plugins**
2. Find "Bulsatcom File Generator"
3. Toggle the switch to **Disable**
4. Restart Jellyfin

### To Completely Remove:
1. Go to **Dashboard → Plugins**
2. Find "Bulsatcom File Generator"
3. Click the **Delete** button (trash icon)
4. Restart Jellyfin
5. **Important:** Manually delete the plugin data folder if needed:
   - Linux: `/var/lib/jellyfin/plugins/Jellyfin.Plugin.BulsatcomChannel/`
   - Windows: `%AppData%\Jellyfin\plugins\Jellyfin.Plugin.BulsatcomChannel\`
   - Docker: `/config/plugins/Jellyfin.Plugin.BulsatcomChannel/`

## Troubleshooting

### Plugin shows version 0.0.0.0
This was a bug in earlier versions. Update to **v1.1.2** or later. The version is now correctly embedded in the DLL.

### Thumbnail image not showing
The plugin thumbnail should display automatically in the catalog. If not:
1. Clear your browser cache
2. Check that the repository URL is correct
3. Wait a few minutes for GitHub CDN to update

### Playback Error after M3U refresh
**How it works:** Bulsatcom tokens/URLs expire after a period, and when the plugin generates new files, Jellyfin needs to reload them.

If you see errors like:
```
[ERR] Error processing request. URL "POST" "/Items/.../PlaybackInfo"
```

**Solutions:**
1. ✅ **Set automatic refresh interval** in Live TV settings (Dashboard → Live TV → Tuner Devices → M3U Tuner → Refresh Interval)
2. **Manual refresh:** Dashboard → Scheduled Tasks → Refresh Guide (Run Now)
3. Check plugin logs after scheduled task runs - you'll see a message reminding you to refresh

**Note:** Jellyfin automatically refreshes the guide based on your configured interval. The plugin logs will notify you when new files are generated.

### Files not generating
1. Check your Bulsatcom credentials in plugin settings
2. Run the scheduled task manually from **Dashboard → Scheduled Tasks**
3. Check Jellyfin logs for errors

## Development

### MD5 Checksum Generation

The MD5 checksum in `manifest.json` is **automatically generated** by GitHub Actions:

- ✅ **Development builds:** Every push to `main` updates the manifest with new checksum
- ✅ **Release builds:** Creating a tag (v1.0.0) triggers full release with updated manifest
- ✅ **Manual builds:** Can be triggered via GitHub Actions interface

### Building Locally

```bash
# Restore dependencies
dotnet restore

# Build the plugin
dotnet build --configuration Release

# The compiled DLL will be in: bin/Release/net8.0/Jellyfin.Plugin.BulsatcomChannel.dll
```
