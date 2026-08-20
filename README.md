# Quota Wisp for Windows

An original, local-only Windows 11 desktop pet that shows Codex quota. If you are looking for the similar project for MacOS, check [codex-quota-pet](https://github.com/danya-kim99/codex-quota-pet), who inspired me.

## Features

- Transparent always-on-top WPF pet with an original animated moon-and-satellites image.
- Primary and secondary quota, reset time, Standard/Turbo mode.
- Local Codex App Server JSON-RPC; no telemetry and no remote service.
- Automatic reconnect, event refresh, 60-second polling and hover refresh.
- Smooth and pixel tooltip styles, English and Russian UI.
- S/M/L sizes, drag, lock, click-through, multi-monitor position restore.
- Hide in fullscreen apps and user-level Windows startup.
- 30-day local quota history, consumption reactions and weighted interactive objects.

## Tooltip preview

Hover over the moon to open the quota dashboard with the exact remaining percentage, reset time, and 24-hour consumption history.

<p align="center">
  <img src="docs/images/screenshot1.png" alt="Quota Wisp dashboard displayed when hovering over the moon" width="296">
</p>

## Moon quota states

The remaining quota is displayed in 10% bands. The full moon is used for 91–100%, and the thinnest waning phase is used for 0–10%.

<table>
  <tr>
    <td align="center"><img src="Assets/quota-wisp.png" alt="Full moon for 91 to 100 percent quota" width="140"><br><strong>Quota: 91–100%</strong></td>
    <td align="center"><img src="Assets/quota-wisp-90.png" alt="Waning moon for 81 to 90 percent quota" width="140"><br><strong>Quota: 81–90%</strong></td>
    <td align="center"><img src="Assets/quota-wisp-80.png" alt="Waning moon for 71 to 80 percent quota" width="140"><br><strong>Quota: 71–80%</strong></td>
    <td align="center"><img src="Assets/quota-wisp-70.png" alt="Waning moon for 61 to 70 percent quota" width="140"><br><strong>Quota: 61–70%</strong></td>
    <td align="center"><img src="Assets/quota-wisp-60.png" alt="Waning moon for 51 to 60 percent quota" width="140"><br><strong>Quota: 51–60%</strong></td>
  </tr>
  <tr>
    <td align="center"><img src="Assets/quota-wisp-50.png" alt="Half moon for 41 to 50 percent quota" width="140"><br><strong>Quota: 41–50%</strong></td>
    <td align="center"><img src="Assets/quota-wisp-40.png" alt="Waning moon for 31 to 40 percent quota" width="140"><br><strong>Quota: 31–40%</strong></td>
    <td align="center"><img src="Assets/quota-wisp-30.png" alt="Waning crescent for 21 to 30 percent quota" width="140"><br><strong>Quota: 21–30%</strong></td>
    <td align="center"><img src="Assets/quota-wisp-20.png" alt="Waning crescent for 11 to 20 percent quota" width="140"><br><strong>Quota: 11–20%</strong></td>
    <td align="center"><img src="Assets/quota-wisp-10.png" alt="Waning crescent for 0 to 10 percent quota" width="140"><br><strong>Quota: 0–10%</strong></td>
  </tr>
</table>

## Run

Extract `QuotaWisp-win-x64.zip` and run `QuotaWisp.exe`. The build is self-contained and does not require .NET to be installed. Windows SmartScreen may show an unknown-publisher warning because the executable is not code-signed.

Quota Wisp looks for `codex.exe`, `codex.cmd`, or `codex.ps1` in standard locations and `PATH`. A custom path can be selected from the tray menu.

Settings and history are stored under `%LOCALAPPDATA%\QuotaWisp`. Enabling startup adds the current executable to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Build and verify

```powershell
dotnet run --project .\Tests\QuotaWisp.SelfTest.csproj
.\build-portable.ps1
```

The project targets Windows 11 x64 and .NET 8 WPF. It has no third-party runtime dependencies.

## Artwork

`Assets/quota-wisp.png` is an original AI-generated project asset created for this Windows implementation. Generation prompt: a friendly full moon with soft craters; transparent background; polished pixel-art-inspired 2D sprite; no sun, planets, text, logos, trademarks, or existing character resemblance. It was generated with the built-in image generation tool.
