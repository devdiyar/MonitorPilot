# Compatibility and troubleshooting

Target: **Windows 10/11 x64**, self-contained WPF app with .NET 10. macOS, Linux, x86, and native ARM64 builds are not included. Physical controls under ARM emulation have not been tested.

## Tested scope

| Configuration | Evidence |
| --- | --- |
| Windows 11 x64; Intel UHD 770 and NVIDIA RTX 4070 SUPER; five external displays | Physical brightness/RGB readback/restore, live queue stress, local app use. |
| AOC Q32G2WG3, 27G15N, 27G1G4; LG W2243; Dell P2419H | Brightness and RGB tested at this workstation; not every firmware/input/picture mode. |
| Small scaled work areas and narrow WPF pages | Automated window-bound and stacked-control checks. Full physical DPI/hotplug coverage remains open. |
| Windows 10 x64 | Intended compatibility; not physically tested here. |
| Other monitors, docks, DisplayLink, TVs, remote sessions, mirrored displays | Further device testing needed. Hardware support is detected per control. |

App compatibility is separate from runtime servicing. Microsoft's [.NET 10 supported-OS list](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) includes selected Windows 10 Enterprise/IoT LTSC editions and supported Windows 11 releases. It does not promise support for every out-of-support Windows 10 edition. Bundling the runtime does not change its policy.

## Availability

| Function | Requirement / limitation |
| --- | --- |
| Discovery, main display, focus, extend, layouts | Local Windows desktop and graphics driver supporting Windows display configuration. Remote/clone modes need separate testing. |
| Brightness, contrast, RGB | Active external monitor with readable/writable DDC/CI controls; each detected independently. |
| White-point presets | Advertised monitor presets. May reset RGB inside the monitor. |
| Internal notebook brightness | WMI/internal-panel backend not implemented; software dimming is available. |
| HDR | Status and Windows shortcut; HDR switching/calibration not implemented. |
| ICC and gamma | Windows tools and visual patterns; no automatic assignment or gamma-ramp changes. |
| Window restore | Same window and process still open. Elevation, exclusive fullscreen, virtual desktops and app restrictions can prevent it. |
| Software dimming | Click-through overlay; exclusive fullscreen may cover it. No backlight change. |
| Standby | Windows monitor-power command; desktop retained; wake via mouse/keyboard. |

## Missing hardware controls

1. Enable **DDC/CI** in the monitor's OSD. Choose a picture mode permitting manual adjustment; HDR and dynamic contrast may lock it.
2. Try direct HDMI/DisplayPort when using a dock/adapter. DDC passthrough varies.
3. Use **Werte neu erkennen**. For stale support information choose **DDC/CI-Funktionen erneut prüfen**; it can take several seconds.
4. Close competing monitor-control tools while diagnosing. Report model, connection path, driver and affected control.

Unsupported controls stay disabled. Writes are read back; mismatches/unavailable responses are reported. Some monitors implement MCCS incompletely, so discovery cannot certify every device. Microsoft documents this [monitor API limitation](https://learn.microsoft.com/en-us/windows/win32/api/highlevelmonitorconfigurationapi/nf-highlevelmonitorconfigurationapi-setmonitorbrightness).

## Slow response

Sliders update their target immediately while hardware commands run off the UI thread. Intermediate pending values are replaced by newer ones; readback confirms the result. The panel and driver can still take hundreds of milliseconds. [Performance](PERFORMANCE.md) documents measured timings.

Workspace snapshots and full rediscovery read more than one brightness control. Capability rechecks are intended for capability changes, not every slider move.

## Incomplete restore

Keep apps open between focus and restore. Reconnecting to another GPU port or replacing a monitor can change its identity; save a fresh profile for that setup. If a display change is unusable, wait for guardian rollback or choose **Sofort zurücksetzen**.

The guardian restores topology and open-window placements. Workspace restoration attempts hardware values and dimming after the new topology is accepted. Unreachable controls are reported; focus backup stays available if hardware restoration is incomplete.

## Data recovery and removal

App data lives in `%LOCALAPPDATA%\MonitorPilot`. Malformed startup data is backed up as `.corrupt-… .bak` before fallback saves. Copy the folder before manual repairs. Optional capability-cache failures are logged and the cache can be rebuilt.

Windows uninstall removes the executable, app shortcuts, autostart and uninstall entries while preserving profiles/backups. Portable use shares the same app-data folder.

Review profiles/diagnostics/logs before public issue submission. Raw local QA data is intentionally excluded from the repository.
