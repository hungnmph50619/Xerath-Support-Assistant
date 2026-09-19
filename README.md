# Xerath Support Assistant · Aim Lab V0.1

An initial, **working-source C#/.NET 8 WPF project** that demonstrates a reusable Q/W/E/R target-prediction engine in an independent practice arena. The UI and on-screen descriptions are in Vietnamese.

## What V0.1 actually does

- Simulated target moves continuously; enable random direction changes, alter speed or drag the target with the mouse.
- Predict a Q line aim after an adjustable *already elapsed* charge duration; W and R AoE centers; E projectile interception with cast windup and an optional blocking minion.
- Visualize a predicted aim point, skill line/area and target's current movement vector.
- Cast each simulated spell and compare its fixed prediction against the target's **actual later position**; track hit/miss results.
- Run a dependency-free console test program for the C# core.

## Not included in this first version

This prototype **does not capture the League client, analyze the live game, draw an in-game overlay, send input, handle summoner spell tracking, or provide live gank alerts**. Its distances, cast delays and collision shapes are intentionally practice-space values, **not verified League of Legends patch data**. The near/arrived ally-jungle alert (Vietnamese voice at 70%, 30-second suppression) is a planned separate module, not a delivered feature here.

Direct live aiming assistance and timing opponents' spells can violate Riot's third-party software rules and carry account risk. This project does not circumvent Vanguard. Evaluate policies/permissions before considering any live gameplay integration.

## Requirements

- Windows 10/11, .NET 8 SDK (desktop development / WPF support).
- No extra NuGet dependencies, model downloads, game installation or elevated permissions.

## Run on Windows

```powershell
cd .\XerathSupportAssistant\src\XerathAssistant.Desktop
dotnet run
```

Or open `src/XerathAssistant.Desktop/XerathAssistant.Desktop.csproj` in Visual Studio with the `.NET desktop development` workload and press F5.

Use the spell dropdown, Q charge and speed sliders. Enable `Đổi hướng` to increase difficulty; turn on `Lính chắn E` to test blockers. Click `Thử tung chiêu` to cast. Drag the red target to set up a situation.

## Core tests

```powershell
cd .\XerathSupportAssistant\tests\XerathAssistant.CoreTests
dotnet run
```

This runs 11 assertion-based core math tests without a test framework or external packages.

## Folder structure

```
src/XerathAssistant.Core/      pure .NET math, prediction and collision checks
src/XerathAssistant.Desktop/   WPF Windows desktop practice visualization
tests/XerathAssistant.CoreTests/ executable smoke tests for prediction math
```

## Next implementation milestones

1. Add video-file input, image-to-screen calibration and a manually labeled target detector; measure prediction error on replayable sequences.
2. Implement minimap analysis on **recorded video** with explicit player/side calibration and stable identity tracking.
3. Add a separate ally-jungle Near/Arrived state machine, configurable Vietnamese TTS (70%) and a 30-second per-approach cooldown, tested against labeled clips.
4. Add offline reference/calculation data for Xerath's skills/damage, maintain patch-specific source and versioning, then use that data in the practice app.

This is V0.1, **not** an assertion that later modules or game integration are already implemented.
