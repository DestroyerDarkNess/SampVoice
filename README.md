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

## Step-by-Step Deployment

### 1. Deploy the Voice Relay (Server)
The relay server routes voice packets between nearby players. Use Docker for the most professional, "one-click" deployment:

1.  **Build & Publish**: 
    - Go to the `ServerRelay/` folder.
    - Run `publish.bat`.
    - Provide your Docker Hub username and any image name (e.g., `my-voice-relay`).
    - The script will build, log in, and push the image to `docker.io`.
2.  **Host on Render.com (FREE)**:
    - Create a **New Web Service** on Render.
    - Choose **"Deploy an image from a Docker registry"**.
    - Enter `docker.io/YOUR_USER/IMAGE_NAME:latest`.
    - Render will provide a URL like `wss://your-relay.onrender.com`.

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
