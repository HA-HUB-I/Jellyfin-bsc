# 🛠️ Jellyfin Bulsatcom Plugin - Configuration Guide

## Configuration Page Features

The Bulsatcom File Generator plugin provides a comprehensive configuration interface with the following sections:

### 🔐 Account Settings
- **Username** (Required): Your Bulsatcom account username
- **Password** (Required): Your Bulsatcom account password
- **Test Connection Button**: Validates credentials without saving

### 🌐 API Settings
- **API URL**: Bulsatcom API endpoint (default: https://api.iptv.bulsat.com)
- **Device Type**: Choose between Samsung TV (recommended) or PC Web
- **Connection Timeout**: 5-120 seconds (default: 30)
- **Max Retries**: 1-10 retry attempts on failures (default: 3)

### 🔄 Refresh Settings
- **Refresh Interval**: 1-24 hours (default: 6 hours)
- **Download EPG**: Enable/disable electronic program guide

### 📁 File Settings
- **File Location Display**: Shows where generated files are stored
- **M3U Filename**: Custom name for playlist file (default: bulsat.m3u)
- **EPG Filename**: Custom name for EPG file (default: bulsat.xml)

### 🎭 Content Filtering
- **Blocked Genres**: Comma-separated list of genres to exclude

### 🐛 Debug Settings
- **Debug Logging**: Enable detailed logging
- **Validate on Save**: Test connection when saving configuration

### 📊 Status Information
- **Last Successful Update**: Timestamp of last successful run
- **Total Channels**: Number of channels found
- **Last Error**: Most recent error message

## Generated Files Location

Files are saved to: `/config/data/plugins/Jellyfin.Plugin.BulsatcomChannel/`

### Files Generated:
1. **M3U Playlist** (`bulsat.m3u`): Channel list for Live TV
2. **EPG XML** (`bulsat.xml`): Electronic program guide data

## Error Handling

The plugin includes comprehensive error handling:

- ✅ **Credential Validation**: Warns if username/password are empty
- ✅ **Connection Testing**: Test button to verify API access
- ✅ **Retry Logic**: Automatic retry with exponential backoff
- ✅ **Error Persistence**: Errors are saved and displayed in the UI
- ✅ **Timeout Handling**: Configurable connection timeouts
- ✅ **Input Validation**: Prevents invalid configuration values

## How to Configure

1. **Install the Plugin** via Jellyfin's plugin repository
2. **Go to Settings** → **Plugins** → **Bulsatcom File Generator**
3. **Enter Credentials** in the Account Settings section
4. **Test Connection** to verify your credentials work
5. **Adjust Settings** as needed (refresh interval, file names, etc.)
6. **Save Configuration**
7. **Run Now** or wait for automatic execution

## Live TV Setup

After successful configuration:

1. Go to **Live TV** settings in Jellyfin
2. Add **M3U Tuner** with path: `/config/data/plugins/Jellyfin.Plugin.BulsatcomChannel/bulsat.m3u`
3. Add **XMLTV EPG** with path: `/config/data/plugins/Jellyfin.Plugin.BulsatcomChannel/bulsat.xml`

## Troubleshooting

### Common Issues:

1. **"Username and password are required"**
   - Enter valid Bulsatcom credentials in Account Settings

2. **"Connection timeout"**
   - Increase timeout value in API Settings
   - Check internet connection

3. **"Invalid credentials"**
   - Verify username/password are correct
   - Use Test Connection button to validate

4. **"No channels found"**
   - Check if your Bulsatcom account has active subscription
   - Try different Device Type (Samsung TV vs PC Web)

5. **"Files not found in Live TV"**
   - Verify file paths match the generated files location
   - Check that the scheduled task completed successfully

The enhanced configuration page provides visual feedback, validation, and detailed status information to make the setup process as smooth as possible.