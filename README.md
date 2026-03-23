# Universal Voice Chat for SA:MP

**Universal Voice Chat** is a high-performance, proximity-based voice chat plugin for Grand Theft Auto: San Andreas (SA:MP). Unlike traditional mods, it is **100% client-side** (Universal), meaning it works on any server without requiring server-side plugins or cooperation from server owners.

Built with **C# Native AOT**, it targets 32-bit `win-x86` for direct game memory injection and zero-dependency installation.

---

## Features

- **Native AOT Performance**: Compiled to a native 5MB `.asi` plugin with minimal CPU overhead.
- **WebSocket Binary Relay**: Uses low-latency binary transport, optimized for free cloud hosting (Render, MonsterASP, Railway).
- **3D Spatial Audio**: Real-time volume and positioning calculated based on in-game coordinates.
- **Opus Compression**: High-fidelity 48kHz mono audio compressed to 48kbps for crystal-clear voice with zero network lag.
- **Jitter Buffer**: Smooth playback even with network packet bursts (~80ms pre-buffer).
- **Volume Boost**: 2.5x output amplification for clear, audible voice chat.
- **Push-to-Talk (PTT)**: Configurable keybound activation (Default: `B`).
- **Auto-Configuration**: Dynamically creates `UniversalVoiceChat.ini` on first run.
- **Pure P/Invoke Audio**: Direct `winmm.dll` calls for both capture and playback, fully compatible with Native AOT (no NAudio callbacks).

---

## Deployment

### 1. Deploy the Voice Relay (Server)

#### Option A: Quick Deployment (Recommended)
Use the pre-built Docker image for a "one-click" setup on Render.com:
1.  Log in to [Render.com](https://render.com).
2.  Click **New +** > **Web Service**.
3.  Select **"Deploy an image from a Docker registry"**.
4.  Enter the public image: `docker.io/pablisamp/sampvoice:latest` (or your own published image).
5.  Render will provide a URL like `wss://your-relay.onrender.com`.

#### Option B: Manual Docker Publish
If you want to use your own Docker Hub repository:
1.  Go to the `ServerRelay/` folder.
2.  Run `publish.bat`.
3.  Provide your Docker Hub username and image name. The script handles the build, login, and push automatically.
4.  Follow the Render deployment steps above using your new image URL.

### 2. Configure the Plugin (Client)
1.  Open `UniversalVoiceChat.ini` (created after first run or manually created next to `gta_sa.exe`).
2.  Set the `Url` to your Render WebSocket URL:
    ```ini
    [Relay]
    Url=wss://your-relay.onrender.com
    ```
3.  (Optional) Adjust `MaxDistance` or `PushToTalkKey`.

### 3. Share the Plugin
- Distribute the `UniversalVoiceChat.asi` and `UniversalVoiceChat.ini` to your players.
- They simply drop both files into their main GTA San Andreas folder.

---

## Configuration (UniversalVoiceChat.ini)

| Section | Key | Description | Default |
| :--- | :--- | :--- | :--- |
| **Relay** | `Url` | The WebSocket URL of your hosted relay server. | `ws://localhost:8000` |
| **Audio** | `MaxDistance` | Max distance in game units to hear others. | `40.0` |
| **Audio** | `InputGain` | Microphone volume multiplier. | `1.0` |
| **Audio** | `OutputGain` | Incoming voice volume multiplier. | `1.0` |
| **Activation** | `PushToTalkKey` | Windows Virtual Key Code for talking. | `0x42 (B Key)` |

---

## Technical Stack

- **Client**: C# 12.0 (.NET 9) Native AOT (`win-x86`)
- **Audio Engine**: Pure `winmm.dll` P/Invoke (waveIn/waveOut with native memory headers)
- **Compression**: Concentus (Pure C# Opus Implementation, 48kbps VOIP mode)
- **Networking**: System.Net.WebSockets (Binary Transport, thread-safe)
- **Relay Server**: Python 3.11 (websockets + asyncio)
- **Container**: Docker (hub.docker.com)

---

> [!IMPORTANT]
> This mod does NOT require any server-side scripts. It detects the SA:MP server IP/Port automatically to create private voice "rooms" for each server you join.
