# Contributing to MonitorPilot

Small, reproducible fixes and monitor reports are especially useful. Describe the user-visible problem, the change, and its verification. Avoid bundling unrelated changes.

## Development

Use Windows x64 and the .NET 10 SDK. The app uses WPF, WinForms for its tray menu, and direct Win32 APIs for display configuration and DDC/CI. There are no external app package dependencies.

```powershell
dotnet build src/MonitorPilot/MonitorPilot.csproj -c Release
./scripts/build.ps1
./scripts/check.ps1
```

`build.ps1` publishes an uncompressed self-contained executable and tests that exact file. `check.ps1` defaults to self-tests and the WPF harness. Both work without attached DDC monitors. The harness uses isolated temporary data and a simulated hardware boundary while exercising actual app/tray controls.

## Source map

| Files | Responsibility |
| --- | --- |
| DisplayService.cs, Native.cs | Windows display topology, modes, native declarations |
| HardwareService.cs, LiveControlQueue.cs | DDC/CI, capability cache, readback, latest-value queues |
| AppController.cs, Models.cs | Workspaces, settings, profiles, persistence, validation |
| Recovery.cs, WindowService.cs | Independent guardian and open-window placement |
| MainWindow.cs, Dashboard.cs, Pages.cs, MonitorControlsPage.cs | WPF pages |
| TrayQuickWindow.cs, TrayMenu.cs, TrayTheme.cs | Tray controls |
| Theme.cs, UI/Controls.xaml, OverlayService.cs | Themes, templates, dimming, patterns |
| SelfTests.cs, CompatibilityTests.cs, LiveControlTests.cs, LiveUiBenchmark.cs | Hardware-independent checks |

Live changes must preserve the latest target, serialize native writes per graphics adapter, verify readback, and drain before capture, topology changes, or exit. Preserve original hardware data and user profiles. Keep monitor I/O off the WPF dispatcher.

## Physical device tests

These checks are opt-in:

```powershell
./scripts/check.ps1 -Diagnose           # Read-only discovery; writes a local report
./scripts/check.ps1 -Hardware           # Brightness change and restoration
./scripts/check.ps1 -LiveHardware       # Live queue burst and restoration
./scripts/check.ps1 -Focus              # Temporarily disable extra outputs, then restore
./scripts/check.ps1 -Timeout            # Guardian's automatic rollback
```

Run write tests only when display changes are acceptable and no person or other tool is adjusting monitor settings. The routines save the immediate pre-test state and restore it in `finally`; failed readback is reported. CI cannot certify physical DDC behavior and does not run these tests.

## Documentation images

```powershell
./artifacts/release/MonitorPilot.exe --export-demo-images docs/images
```

This renders actual WPF components with anonymous data, without native display reads or hardware writes. Label documentation examples as examples.

## Report a problem

Include app version, Windows build, monitor model, GPU/driver, connection path, DDC/CI setting, affected control, exact steps, and expected result. For latency, distinguish a stuttering UI from a slow physical panel.

Review diagnostics, profiles, logs, and screenshots before uploading. Profiles may contain window titles and monitor identifiers. Do not commit artifacts, personal settings/profiles, logs, or credentials.

For sensitive vulnerability reports, use the maintainer's contact route on the [GitHub profile](https://github.com/devdiyar) rather than attaching private data to a public issue. A dedicated private disclosure channel has not yet been configured.

Contributions use the repository's MIT license. Third-party code and assets must retain applicable notices.
