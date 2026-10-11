MonitorPilot 1.3.0 introduces live picture controls throughout the app and tray. Brightness, contrast, RGB, supported white points, and software dimming apply as you move the controls; the manual brightness/color Apply buttons are gone.

Choose **MonitorPilot-Setup-win-x64.zip** for installation or **MonitorPilot-Portable-win-x64.zip** for use without installation. Extract the full folder first. Both include the .NET runtime, license notices, and a quick-start guide. The interface is German; builds are currently unsigned. SHA256SUMS.txt and BUILD-INFO.json describe these exact release assets.

Other changes: latest-value queues with verified monitor readback, fewer redundant hardware delays, existing-control updates, narrow-page layouts, recovery of incomplete/damaged settings, stricter profile validation, and guardian startup through the dotnet host.

Validation: 47 self-tests and 14 WPF integration checks against the published executable. Physical Windows 11 testing covered five external monitors; Windows 10 and other devices remain subject to further testing. CI results establish build/UI behavior, not universal DDC/CI support or measured energy savings.

[Documentation](https://github.com/devdiyar/MonitorPilot) · [Deutsch](https://github.com/devdiyar/MonitorPilot/blob/main/docs/README.de.md) · [Compatibility](https://github.com/devdiyar/MonitorPilot/blob/main/docs/COMPATIBILITY.md) · [Changelog](https://github.com/devdiyar/MonitorPilot/blob/main/CHANGELOG.md)
