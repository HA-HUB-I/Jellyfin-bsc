# Contributing to Jellyfin Bulsatcom Channel Plugin

Thank you for considering contributing to **Jellyfin.Plugin.BulsatcomChannel**!

Whether you are reporting a bug, proposing an improvement, improving documentation, submitting channel logos, or contributing code, we appreciate your help.

---

## ⚖️ Legal Disclaimer & Notice

- **No Affiliation**: This open-source project is an independent community development and is **not affiliated with, endorsed by, sponsored by, or associated with Bulsatcom EAD (Булсатком)** in any manner.
- **No Service or Stream Hosting**: This repository and its maintainers **do not host, transmit, stream, rebroadcast, sell, or provide any media content or IPTV subscription services**.
- **Personal & Legal Use Only**: This software is strictly a helper utility intended solely for existing Bulsatcom subscribers to access their own legitimately subscribed services on their private Jellyfin server using their own personal account credentials.
- **No Circumvention / Anti-Piracy**: Contributions that attempt to bypass DRM, crack authentication, distribute copyrighted streams, or enable unauthorized access will be strictly rejected.
- **Credential Protection**: Never commit or share real account credentials, passwords, session tokens, or private playlist URLs in issues or pull requests.

---

## 🛠️ Development & Building

The plugin multi-targets both **.NET 10** (for Jellyfin 12.0+) and **.NET 9** (for Jellyfin 10.11.x).

### Prerequisites
- [.NET SDK 9.0](https://dotnet.microsoft.com/download/dotnet/9.0) and [.NET SDK 10.0](https://dotnet.microsoft.com/download/dotnet/10.0) (or latest .NET SDK supporting both runtimes)
- Git

### Building Locally
```bash
# Clone the repository
git clone https://github.com/HA-HUB-I/Jellyfin-bsc.git
cd Jellyfin-bsc

# Restore dependencies
dotnet restore

# Build release packages for both Jellyfin 12 (.NET 10) and Jellyfin 10.11 (.NET 9)
dotnet build --configuration Release
```

Output assemblies will be located at:
- `bin/Release/net10.0/Jellyfin.Plugin.BulsatcomChannel.dll`
- `bin/Release/net9.0/Jellyfin.Plugin.BulsatcomChannel.dll`

---

## 🚀 Submitting Pull Requests

1. **Fork and Branch**: Create a feature branch off `master` with a descriptive name (e.g. `feat/enhance-epg`, `fix/login-retry`).
2. **Conventional Commits**: Please use standard Conventional Commit prefixes:
   - `feat:` for new features or improvements.
   - `fix:` for bug fixes.
   - `docs:` for documentation updates.
   - `chore:` for maintenance or workflow changes.
3. **Multi-Target Verification**: Ensure that the solution compiles without warnings or errors across both `net10.0` and `net9.0`.
4. **Logos Submissions**: Any new channel logos should be transparent, high-contrast PNG files added to `bulsat_logos.zip` named identically to `{channel.EpgName}.png`.

---

## 🐛 Issues & Support

- **Bug Reports**: Please include your Jellyfin server version, plugin version, host OS / deployment type (Docker, Linux, Windows), and sanitized logs.
- **Feature Requests**: Describe the desired feature, expected behavior, and use case in detail.
