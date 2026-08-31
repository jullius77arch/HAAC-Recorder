
<div align="center">

# 🎙️ HAAC Recorder

**Raw, unprocessed PCM audio recording for Windows Phone 8.1**

![Platform](https://img.shields.io/badge/platform-Windows%20Phone%208.1-0078D7?logo=windows&logoColor=white)
![Language](https://img.shields.io/badge/language-C%23-239120?logo=csharp&logoColor=white)
![IDE](https://img.shields.io/badge/built%20with-Visual%20Studio%202013-5C2D91?logo=visualstudio&logoColor=white)
![License](https://img.shields.io/badge/license-GPLv3-blue)

</div>

---

Most recording apps run audio through AGC, echo cancellation, and noise suppression. **HAAC Recorder tries to skip all of that**, pulling raw audio straight from the HAAC mics found on select Nokia Lumia phones — ideal for loud sources like concerts or instruments. This app is not meant for voice recordings when used in RAW mode, as it does not provide any auto gain to boost the sound.

## 🎯 How It Works

At the moment you hit **Start**, the app negotiates the best available quality automatically, trying each combination in order until one works:

| Priority | Channels | Processing | Result |
|:---:|:---:|:---:|---|
| 1 | Stereo | Raw | 🥇 Best case — two channels, no processing |
| 2 | Stereo | Standard | Two channels, driver applies its own processing |
| 3 | Mono | Raw | One channel, no processing |
| 4 | Mono | Standard | Fallback of last resort |

## 📁 Output

| Property | Value |
|---|---|
| Format | 16-bit PCM WAV, 48kHz |
| Channels | Stereo, or mono if the device requires it |
| Location | `Music\recordings\` on device |

## 🛠️ Build & Run

1. Open `HaacRecorder.sln` in **Visual Studio 2013 (Update 5)** with the WP8.1 SDK installed.
2. Pick a target platform (`x86`, `ARM`, or `Any CPU`).
3. Deploy to a physical Lumia device (ARM), or run in the WP8.1 emulator.

> The emulator has no HAAC hardware, so it will always land on stereo standard — useful for testing the fallback path, not for judging audio quality.

A pre-built `.appx` release will also be made available for sideloading, for anyone who'd rather skip building from source.

## 💬 Feedback

Don't have a GitHub account but hit a bug? Email **start-07axed@icloud.com**.

I'm also just as interested in hearing:
- Whether you find the app useful
- Your phone model, and whether it supports RAW audio capture

Known Supported Lumia HAAC phones:

- 1520 RAW
- 928 standard

## License

This project is licensed under the GNU General Public License v3.0 — see the [LICENSE](LICENSE) file for details.

---

<div align="center">
<sub>Built with the help of <a href="https://claude.ai">Claude</a> 🤖</sub>
</div>
