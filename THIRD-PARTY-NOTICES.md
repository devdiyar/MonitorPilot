# Third-party notices

MonitorPilot's own source, documentation, and documentation logo use the root [MIT license](LICENSE).

Self-contained executables bundle Microsoft .NET and Windows Desktop runtime components. Their notices are included in release ZIPs under `licenses/`. Repository copies correspond to the local runtime packs used for validation, version 10.0.12:

- [.NET runtime license](licenses/DOTNET-LICENSE.txt)
- [.NET runtime third-party notices](licenses/DOTNET-THIRD-PARTY-NOTICES.txt)
- [Windows Desktop / WPF license](licenses/WPF-LICENSE.txt)

The package script obtains notices from the runtime packs selected for that build, so a different SDK ships matching notices. Windows-provided APIs, fonts, and system glyphs are used from the installed OS; OS components are not copied into this source repository.

Public Microsoft documentation and tools such as Monitorian, Twinkle Tray, and ddcutil informed the investigation. Their code, images, icons, and research checkouts are not included in this repository or release packages.
