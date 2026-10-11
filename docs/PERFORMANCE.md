# Performance: UI response and hardware latency

Slider targets update immediately; DDC/CI acknowledgement can take longer. Measurements below were taken on 2026-10-11 on Windows 11 x64 with five external displays and Intel UHD 770/NVIDIA RTX 4070 SUPER. They are workstation measurements, not guarantees.

## Changes

- Discover brightness first, enrich other controls in the background.
- Cache capability lists while reading current values afresh. Available capabilities expire after 30 days; failed queries have a one-day cooldown. Explicit rechecks bypass the cache.
- Reuse display-mode lists and invalidate them on display changes.
- Replace intermediate pending requests with the latest value per monitor/control; merge controls where possible. Serialize shared-adapter traffic; independent adapters progress concurrently.
- Update WPF controls in place instead of rebuilding pages or entering the global busy state.
- Remove unconditional 50 ms sleeps after native setters; retain bounded retries for stale readback.
- Change existing dimming-window opacity.
- Keep managed assemblies uncompressed inside the EXE to avoid startup decompression/private copies. ZIPs compress downloads without changing the EXE.

## Measurements

| Operation | Earlier baseline | After optimization | Scope |
| --- | ---: | ---: | --- |
| Brightness set + readback | 248.8 ms mean | 187.8 ms mean | Small change on each of five monitors; restored. |
| Red gain + color readback | 384.7 ms mean | 324.2 ms mean | Same monitors; color state checked/restored. |
| Warm full refresh | About 9.55 s before caching | About 1.62 s in 1.2 | Earlier same-workstation comparison. |
| First brightness discovered | — | About 305 ms in 1.2 | Earlier staged-discovery measurement. |

A fresh 1.3 release run measured **1,615.5 ms** for warm refresh, **1,635.3 ms** for a workspace capture, and **310 ms** for the first brightness result. The color page rendered in 26.6 ms initially and 4.5 ms on the repeated render; the four discovery/control-preservation checks also passed. These are a read-only app benchmark, not a frame-rate guarantee.

Setter comparisons isolate redundant waits. Other operations, first original-value backup and presets can need additional reads.

A physical live-queue stress run submitted 200 rapid positions per monitor across five monitors plus final targets. It produced **7 native transactions**, accepted the burst in **0.3 ms** at the controller boundary, and confirmed final targets in **1,326.6 ms**. Immediate pre-test values were restored and independently probed. The enqueue time is not graphical frame time or panel latency.

An earlier run took **2,329.7 ms** and missed the 2-second performance budget despite correct targets/restoration. This variation is retained. Bus/driver latency remains a limit; these results do not prove universal sub-2-second completion.

## Investigation

Microsoft documents approximately 50 ms for [SetMonitorBrightness](https://learn.microsoft.com/en-us/windows/win32/api/highlevelmonitorconfigurationapi/nf-highlevelmonitorconfigurationapi-setmonitorbrightness), [SetMonitorRedGreenOrBlueGain](https://learn.microsoft.com/en-us/windows/win32/api/highlevelmonitorconfigurationapi/nf-highlevelmonitorconfigurationapi-setmonitorredgreenorbluegain), and [SetVCPFeature](https://learn.microsoft.com/en-us/windows/win32/api/lowlevelmonitorconfigurationapi/nf-lowlevelmonitorconfigurationapi-setvcpfeature). Round trips include reads and driver communication.

[ddcutil's performance notes](https://www.ddcutil.com/performance_options/) explain protocol waits and monitor-specific behavior. [Twinkle Tray's throttling](https://github.com/xanderfrangos/twinkle-tray/blob/fec2ef0e7a3ad94092f230faa4d6d7b87a0cf188/src/electron.js) and [Monitorian's controller](https://github.com/emoacht/Monitorian/blob/ec87b4a55a05b42e5a833b1d0130e291f3864991/Source/Monitorian.Core/AppControllerCore.cs) informed the investigation. MonitorPilot implements its own queue/verification; their code is not redistributed.

## Reproduce

Build, then use `MonitorPilot.exe --benchmark-app <report.json>` for read-only discovery plus simulated UI writes. `--benchmark-ddc` makes small brightness/red changes and restores them. `--test-live-ddc` tests the queue and restores immediate pre-test state. Physical write tests are opt-in and should not run alongside manual adjustments.

Raw reports contain local display data and stay in ignored artifacts. [Validation](PRUEFBERICHT.md) states the test scope. CPU/GPU reduction from focus mode has not been measured.
