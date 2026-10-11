using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MonitorPilot;

internal static class CompatibilityTests
{
    public static void Register(Action<string,Action> test,TopologySnapshot topology)
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        void Reject(WorkspaceSnapshot value)
        {
            try{Store.Validate(value);}catch(InvalidDataException){return;}
            throw new Exception("Malformed workspace must be rejected before a native display change");
        }
        void Temporary(Action action)
        {
            string before=Store.Root;Store.Root=Path.Combine(Path.GetTempPath(),"MonitorPilot-compatibility",Guid.NewGuid().ToString("N"));
            try{action();}finally{Store.Root=before;}
        }
        test("Incomplete settings retain custom shortcuts and recover missing actions",()=>Temporary(()=>
        {
            Store.Save("settings.json",new AppSettings{Aliases=null!,Hotkeys=new(){new(){Action="show",Modifiers=3,Key=0x70,Label="App öffnen"}},DayBrightness=130,NightBrightness=-2,Appearance="unknown"});
            var c=new AppController();c.Load();
            Check(c.Settings.Hotkeys.Count==7&&c.Settings.Aliases!=null,"Missing settings were not repaired");
            Check(c.Settings.Hotkeys.Single(h=>h.Action=="show").Key==0x70,"A valid custom shortcut was lost");
            Check(c.Settings.DayBrightness==100&&c.Settings.NightBrightness==0&&c.Settings.Appearance=="System","Invalid settings were not bounded");
        }));
        test("Corrupt settings are backed up without preventing valid profiles from loading",()=>Temporary(()=>
        {
            Directory.CreateDirectory(Store.Root);const string broken="{\"Hotkeys\": [";File.WriteAllText(Store.FilePath("settings.json"),broken);
            Store.Save("profiles.json",new List<ProfileEntry>{new(){Workspace=new(){Name="Preserved",Topology=topology}}});
            var c=new AppController();c.Load();
            Check(c.Profiles.Count==1&&c.Profiles[0].Workspace.Name=="Preserved","A different valid data file was lost");
            c.SaveSettings();Check(Directory.EnumerateFiles(Store.Root,"settings.json.corrupt-*.bak").Any(p=>File.ReadAllText(p)==broken),"Original damaged settings were overwritten without backup");
        }));
        test("Corrupt profile collection is preserved before startup recovery",()=>Temporary(()=>
        {
            const string broken="[null]";Directory.CreateDirectory(Store.Root);File.WriteAllText(Store.FilePath("profiles.json"),broken);
            var c=new AppController();c.Load();
            Check(c.Profiles.Count==0&&Directory.EnumerateFiles(Store.Root,"profiles.json.corrupt-*.bak").Any(p=>File.ReadAllText(p)==broken),"Invalid profile entry was not safely recovered");
        }));
        test("Null profile collections receive a clear validation rejection",()=>
        {
            Reject(new(){Topology=null!});Reject(new(){Topology=topology,Windows=null!});Reject(new(){Topology=topology,Hardware=null!});Reject(new(){Topology=topology,Dimming=null!});
        });
        test("Duplicate monitor identities are rejected before applying a profile",()=>
        {
            var value=new WorkspaceSnapshot{Topology=Store.Clone(topology)};value.Topology.Identities[1].Key="A";Reject(value);
        });
        test("Invalid window placement is rejected before applying a profile",()=>
        {
            Reject(new(){Topology=topology,Windows=new(){new(){Handle=1,ProcessId=1,ProcessStart=1,Placement=new byte[43]}}});
        });
        test("Profile hardware and dimming must belong to the saved monitors",()=>
        {
            Reject(new(){Topology=topology,Hardware=new(){{"unknown",new()}}});Reject(new(){Topology=topology,Dimming=new(){{"unknown",10}}});
        });
        test("Invalid white points and empty hardware ranges are rejected",()=>
        {
            Reject(new(){Topology=topology,Hardware=new(){{"a",new(){Temperature=9}}}});
            Reject(new(){Topology=topology,Hardware=new(){{"a",new(){Brightness=new(){Min=20,Max=20,Current=20}}}}});
        });
        test("Recovery launches the application when running through the dotnet host",()=>
        {
            var method=typeof(RecoveryGuard).GetMethod("StartInfo",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            if(method==null)throw new Exception("The watchdog currently launches dotnet without its application assembly");
            var start=(System.Diagnostics.ProcessStartInfo)method.Invoke(null,new object[]{@"C:\runtime\dotnet.exe",@"C:\project\bin"})!;
            Check(start.ArgumentList.Single()==@"C:\project\bin\MonitorPilot.dll"&&!start.UseShellExecute&&start.CreateNoWindow,"Development watchdog command does not include the application assembly");
            var standalone=(System.Diagnostics.ProcessStartInfo)method.Invoke(null,new object[]{@"C:\app\MonitorPilot.exe",@"C:\app"})!;
            Check(standalone.ArgumentList.Count==0,"Standalone launch must use the executable directly");
        });
    }
}
