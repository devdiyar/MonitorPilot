MonitorPilot 1.4.0 adds **English alongside German** throughout the app. Switch instantly using **Settings → Language** or **right-click the tray icon → Language**. The choice is saved and survives restarts. Existing installations keep German by default.

English covers all pages, quick controls, tray menus, calibration guidance, keyboard shortcuts, recovery confirmations, status messages, and app-generated errors. Installer and uninstaller prompts follow your saved language. Windows-owned tools and system dialogs follow your Windows language.

**Brightness, contrast, RGB, supported white points and software dimming still apply live**, including in the tray. Switching languages preserves pending target values, profiles, monitor aliases and custom key bindings, without additional DDC queries.

Choose **MonitorPilot-Setup-win-x64.zip** for installation or **MonitorPilot-Portable-win-x64.zip** for use without installation. Extract the full folder first. Both include the .NET runtime, license notices and quick-start guide. Builds remain unsigned. SHA256SUMS.txt and BUILD-INFO.json describe the release assets.

Validation: **54 self-tests and 23 WPF/tray integration checks** against the published executable, including a language change during live work and immediate language selection from the native tray menu. English light/dark screenshots use the actual WPF components with anonymous sample data. These language tests do not change physical monitors. Previous physical Windows 11 testing covered five external monitors; Windows 10 and other devices still need further device testing.

[Documentation](https://github.com/devdiyar/MonitorPilot) · [Deutsch](https://github.com/devdiyar/MonitorPilot/blob/main/docs/README.de.md) · [Compatibility](https://github.com/devdiyar/MonitorPilot/blob/main/docs/COMPATIBILITY.md) · [Changelog](https://github.com/devdiyar/MonitorPilot/blob/main/CHANGELOG.md)
