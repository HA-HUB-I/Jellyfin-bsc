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
