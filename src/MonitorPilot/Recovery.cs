using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MonitorPilot;

internal sealed class RecoveryResult { public bool Restored {get;set;} public string Error {get;set;}=""; }
internal sealed class RecoveryGuard : IDisposable
{
    public const int TimeoutSeconds=30;
    private readonly string token=Guid.NewGuid().ToString("N");
    private EventWaitHandle? accept,rollback,ready;
    private Process? process;
    private string path="";
    public Stopwatch Elapsed {get;}=new();
    public async Task Arm(WorkspaceSnapshot snapshot)
    {
        Store.Validate(snapshot);
        path=Store.FilePath("pending-"+token+".json");
        Store.Save(Path.GetFileName(path),snapshot);
        accept=new(false,EventResetMode.ManualReset,Name(token,"accept"));
        rollback=new(false,EventResetMode.ManualReset,Name(token,"rollback"));
        ready=new(false,EventResetMode.ManualReset,Name(token,"ready"));
        var start=StartInfo(Environment.ProcessPath!,AppContext.BaseDirectory);
        start.ArgumentList.Add("--watchdog"); start.ArgumentList.Add(path); start.ArgumentList.Add(token);
        process=Process.Start(start) ?? throw new InvalidOperationException(L.T("Wiederherstellungsprozess konnte nicht starten."));
        if(!await Task.Run(()=>ready.WaitOne(TimeSpan.FromSeconds(8)))) { accept.Set(); throw new InvalidOperationException(L.T("Wiederherstellung nicht bereit; Bildschirmänderung wurde abgebrochen.")); }
        Elapsed.Start();
    }
    public async Task<bool> Accept()
    {
        if(Elapsed.Elapsed.TotalSeconds >= TimeoutSeconds-1) return false;
        accept!.Set();
        if(process!=null) await process.WaitForExitAsync();
        return !File.Exists(path+".result");
    }
    public async Task<RecoveryResult> Rollback()
    {
        rollback?.Set();
        if(process!=null) await process.WaitForExitAsync();
        if(File.Exists(path+".result")) return System.Text.Json.JsonSerializer.Deserialize<RecoveryResult>(File.ReadAllText(path+".result"),Store.Json)!;
        return new() {Restored=false,Error=L.T("Der Wiederherstellungsprozess hat kein Ergebnis geliefert.")};
    }
    public void Dispose() { accept?.Dispose(); rollback?.Dispose(); ready?.Dispose(); process?.Dispose(); }
    private static string Name(string token,string part)=>"Local\\MonitorPilot."+token+"."+part;
    internal static ProcessStartInfo StartInfo(string executable,string applicationDirectory)
    {
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        if(Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(Path.Combine(applicationDirectory,"MonitorPilot.dll"));
        return start;
    }
    public static int Watchdog(string path,string token)
    {
        try
        {
            var snapshot=System.Text.Json.JsonSerializer.Deserialize<WorkspaceSnapshot>(File.ReadAllText(path),Store.Json)!;
            Store.Validate(snapshot);
            using var accept=EventWaitHandle.OpenExisting(Name(token,"accept"));
            using var rollback=EventWaitHandle.OpenExisting(Name(token,"rollback"));
            using var ready=EventWaitHandle.OpenExisting(Name(token,"ready"));
            ready.Set();
            int signal=WaitHandle.WaitAny(new WaitHandle[]{accept,rollback},TimeSpan.FromSeconds(TimeoutSeconds));
            if(signal==0) { Store.Log("Display change confirmed."); File.Delete(path); return 0; }
            var result=new RecoveryResult();
            try
            {
                new DisplayService().Apply(snapshot.Topology);
                Thread.Sleep(900);
                new WindowService().Restore(snapshot.Windows);
                result.Restored=true; Store.Log("Guardian restored previous display layout and open windows.");
            }
            catch(Exception ex) {result.Error=ex.Message; Store.Log("Guardian restoration failed: "+ex);}
            File.WriteAllText(path+".result",System.Text.Json.JsonSerializer.Serialize(result,Store.Json));
            return result.Restored ? 0 : 2;
        }
        catch(Exception ex) { Store.Log("Guardian error: "+ex); return 3; }
    }
}
