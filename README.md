# Jellyfin Bulsatcom File Generator Plugin

This plugin for Jellyfin periodically generates an M3U playlist and an XMLTV EPG file from the Bulsatcom IPTV service.

## How it works

This plugin runs as a scheduled task within Jellyfin. It logs into the Bulsatcom API, fetches the channel list and program guide, and saves the data as `bulsat.m3u` and `bulsat.xml` files in the plugin's data directory.

## Installation

1.  Compile the plugin.
2.  Copy the resulting `Jellyfin.Plugin.BulsatcomChannel.dll` to your Jellyfin plugins directory.
3.  Restart Jellyfin.
4.  Go to the Plugins section in the dashboard, find "Bulsatcom File Generator", and configure your username and password.
5.  Run the "Generate Bulsatcom Files" scheduled task.
6.  Check your Jellyfin logs to find the path to the generated `.m3u` and `.xml` files.
7.  Configure your Live TV M3U Tuner and XMLTV EPG source to point to these file paths.
