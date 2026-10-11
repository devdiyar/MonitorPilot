using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace MonitorPilot;
internal sealed class HotkeyService : IDisposable
{
    private readonly IntPtr handle;
    private List<HotkeySetting> registered=new();
    public HotkeyService(IntPtr handle)=>this.handle=handle;
    public void Register(List<HotkeySetting> requested)
    {
        if(requested.Any(k=>k.Modifiers==0 || k.Key==0) || requested.GroupBy(k=>(k.Modifiers,k.Key)).Any(g=>g.Count()>1)) throw new InvalidOperationException("Jedes Kürzel braucht mindestens eine Zusatztaste und muss eindeutig sein.");
        var old=registered;Unregister();var failures=new List<string>();
        for(int i=0;i<requested.Count;i++) if(!Native.RegisterHotKey(handle,9000+i,requested[i].Modifiers|0x4000,requested[i].Key)) failures.Add(Format(requested[i]));
        if(failures.Count>0)
        {
            for(int i=0;i<requested.Count;i++) Native.UnregisterHotKey(handle,9000+i);
            registered=old;for(int i=0;i<old.Count;i++)Native.RegisterHotKey(handle,9000+i,old[i].Modifiers|0x4000,old[i].Key);
            throw new InvalidOperationException("Bereits belegte Tastenkürzel: "+string.Join(", ",failures)+". Bitte unter Einstellungen ändern.");
        }
        registered=Store.Clone(requested);
    }
    public string? Action(int id)=>id>=9000 && id<9000+registered.Count?registered[id-9000].Action:null;
    public static string Format(HotkeySetting setting)=>string.Join(" + ",new[]{(setting.Modifiers&2)!=0?"Strg":null,(setting.Modifiers&1)!=0?"Alt":null,(setting.Modifiers&4)!=0?"Umschalt":null,(setting.Modifiers&8)!=0?"Win":null,((Keys)setting.Key).ToString().Replace("D0","0").Replace("Up","↑").Replace("Down","↓")}.Where(s=>s!=null));
    private void Unregister() {for(int i=0;i<registered.Count;i++)Native.UnregisterHotKey(handle,9000+i);registered=new();}
    public void Dispose()=>Unregister();
}
