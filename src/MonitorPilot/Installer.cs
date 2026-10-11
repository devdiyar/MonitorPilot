using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

namespace MonitorPilot;
internal static class Installer
{
    public static int Install(bool silent)
    {
        try{L.SetLanguage(Store.Read<AppSettings>("settings.json")?.Language);}catch{ /* Language selection must not prevent recovery or installation. */ }
        string source=Environment.ProcessPath!,folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","MonitorPilot"),target=Path.Combine(folder,"MonitorPilot.exe");
        if(!silent && MessageBox.Show(L.F($"MonitorPilot 1.4.0 installieren?\n\nZiel: {folder}\n\nErstellt Startmenü- und Desktop-Verknüpfungen. Bestehende Profile bleiben erhalten. Autostart kann später in der App aktiviert werden."),L.T("MonitorPilot installieren"),MessageBoxButton.OKCancel,MessageBoxImage.Information)!=MessageBoxResult.OK)return 0;
        try
        {
            // Ask the existing process to close; never kill a pending topology confirmation.
            try {using var signal=EventWaitHandle.OpenExisting("Local\\MonitorPilot.Exit");signal.Set();}catch(WaitHandleCannotBeOpenedException){}
            var deadline=DateTime.UtcNow.AddSeconds(8);
            while(DateTime.UtcNow<deadline && Running(target))Thread.Sleep(200);
            if(Running(target))throw new InvalidOperationException(L.T("MonitorPilot führt noch eine Aktion aus. Bitte abschließen und die Installation erneut starten."));
            Directory.CreateDirectory(folder);
            if(!Path.GetFullPath(source).Equals(Path.GetFullPath(target),StringComparison.OrdinalIgnoreCase))
            {
                string temp=target+".new";File.Copy(source,temp,true);
                if(File.Exists(target))File.Copy(target,target+".previous",true);
                File.Move(temp,target,true);
            }
            var shellType=Type.GetTypeFromProgID("WScript.Shell")??throw new InvalidOperationException(L.T("Windows-Verknüpfungsdienst nicht verfügbar."));
            dynamic shell=Activator.CreateInstance(shellType)!;
            try
            {
                string programs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"MonitorPilot");Directory.CreateDirectory(programs);
                Shortcut(shell,Path.Combine(programs,"MonitorPilot.lnk"),target,"",folder);
                string restoreLink=L.T("Arbeitsplatz wiederherstellen.lnk");
                foreach(string oldLink in new[]{"Arbeitsplatz wiederherstellen.lnk","Restore workspace.lnk"})
                {
                    string oldPath=Path.Combine(programs,oldLink);if(oldLink!=restoreLink&&File.Exists(oldPath))File.Delete(oldPath);
                }
                Shortcut(shell,Path.Combine(programs,restoreLink),target,"--restore",folder);
                Shortcut(shell,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"MonitorPilot.lnk"),target,"",folder);
            }
            finally {Marshal.FinalReleaseComObject(shell);}
            string script=Path.Combine(folder,"Uninstall.ps1");
            File.WriteAllText(script,UninstallScript);
            using var registry=Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\MonitorPilot");
            registry.SetValue("DisplayName","MonitorPilot");registry.SetValue("DisplayVersion","1.4.0");registry.SetValue("Publisher","MonitorPilot");registry.SetValue("InstallLocation",folder);registry.SetValue("DisplayIcon",target);registry.SetValue("EstimatedSize",(int)(new FileInfo(target).Length/1024),RegistryValueKind.DWord);
            registry.SetValue("UninstallString","powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \""+script+"\"");registry.SetValue("NoModify",1);registry.SetValue("NoRepair",1);
            Store.Log("Installed to "+target);
            if(!silent) {MessageBox.Show(L.T("MonitorPilot ist installiert. Die App wird jetzt geöffnet.\n\nProfile und Tastenkürzel findest du direkt in der App."),L.T("Installation abgeschlossen"),MessageBoxButton.OK,MessageBoxImage.Information);Process.Start(new ProcessStartInfo(target){UseShellExecute=true});}
            return 0;
        }
        catch(Exception ex){Store.Log("Installation failed: "+ex);if(!silent)MessageBox.Show(ex.Message,L.T("Installation fehlgeschlagen"),MessageBoxButton.OK,MessageBoxImage.Error);return 1;}
    }
    private static bool Running(string path)
    {
        foreach(var p in Process.GetProcessesByName("MonitorPilot"))using(p){try{if(p.Id!=Environment.ProcessId && string.Equals(p.MainModule?.FileName,path,StringComparison.OrdinalIgnoreCase))return true;}catch{}}
        return false;
    }
    private static void Shortcut(dynamic shell,string path,string target,string args,string folder)
    {
        dynamic shortcut=shell.CreateShortcut(path);try{shortcut.TargetPath=target;shortcut.Arguments=args;shortcut.WorkingDirectory=folder;shortcut.IconLocation=target+",0";shortcut.Description=L.T("MonitorPilot · Monitorsteuerung und Arbeitsplatzprofile");shortcut.Save();}finally{Marshal.FinalReleaseComObject(shortcut);}
    }
    private const string UninstallScript="""
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework
$isEnglish = $false
try { $settingsFile = Join-Path $env:LOCALAPPDATA 'MonitorPilot\settings.json'; if (Test-Path -LiteralPath $settingsFile) { $isEnglish = (Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json).Language -eq 'en' } } catch { }
function Text($de, $en) { if ($isEnglish) { $en } else { $de } }
if ([System.Windows.MessageBox]::Show((Text 'MonitorPilot deinstallieren? Profile und Sicherungen bleiben erhalten.' 'Uninstall MonitorPilot? Profiles and backups will be preserved.'), 'MonitorPilot', 'YesNo', 'Question') -ne 'Yes') { exit }
$expected = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\MonitorPilot'))
$actual = [IO.Path]::GetFullPath($PSScriptRoot)
if ($actual -ne $expected) { throw (Text 'Unerwarteter Installationspfad. Abbruch.' 'Unexpected installation path. Aborting.') }
$appExe = Join-Path $actual 'MonitorPilot.exe'
if (Test-Path -LiteralPath $appExe) { Start-Process -FilePath $appExe -ArgumentList '--exit' -WindowStyle Hidden -Wait }
Start-Sleep -Seconds 2
$active = Get-Process MonitorPilot -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $appExe }
if ($active) { [System.Windows.MessageBox]::Show((Text 'Bitte die laufende Monitoraktion abschließen und MonitorPilot beenden.' 'Please finish the current monitor operation and exit MonitorPilot.')); exit 1 }
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -LiteralPath $run -Name MonitorPilot -ErrorAction SilentlyContinue
$programs = Join-Path ([Environment]::GetFolderPath('Programs')) 'MonitorPilot'
$desktopLink = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'MonitorPilot.lnk'
foreach ($name in @('MonitorPilot.lnk','Arbeitsplatz wiederherstellen.lnk','Restore workspace.lnk')) {
    $link = Join-Path $programs $name
    if (Test-Path -LiteralPath $link) { Remove-Item -LiteralPath $link }
}
if ((Test-Path -LiteralPath $programs) -and @(Get-ChildItem -LiteralPath $programs -Force).Count -eq 0) { Remove-Item -LiteralPath $programs }
if (Test-Path -LiteralPath $desktopLink) { Remove-Item -LiteralPath $desktopLink }
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MonitorPilot'
if (Test-Path -LiteralPath $uninstallKey) { Remove-Item -LiteralPath $uninstallKey }
foreach ($name in @('MonitorPilot.exe','MonitorPilot.exe.previous','MonitorPilot.exe.new','Uninstall.ps1')) {
    $file = Join-Path $actual $name
    if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
}
if (@(Get-ChildItem -LiteralPath $actual -Force).Count -eq 0) { Remove-Item -LiteralPath $actual }
[System.Windows.MessageBox]::Show((Text 'MonitorPilot wurde deinstalliert. Deine Profile bleiben unter %LOCALAPPDATA%\MonitorPilot erhalten.' 'MonitorPilot was uninstalled. Your profiles remain in %LOCALAPPDATA%\MonitorPilot.'), 'MonitorPilot')
""";
}
