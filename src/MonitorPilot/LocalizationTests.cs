using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MonitorPilot;
internal static class LocalizationTests
{
    public static void Register(Action<string,Action> test)
    {
        void Check(bool value){if(!value)throw new InvalidOperationException("Localization assertion failed");}
        void In(string language,Action action){string before=L.Language;try{L.SetLanguage(language);action();}finally{L.SetLanguage(before);}}
        test("Language defaults preserve German and reject unknown settings",()=>
        {
            Check(new AppSettings().Language=="de");var settings=new AppSettings{Language="fr"};settings.Normalize();Check(settings.Language=="de");
            settings.Language=null!;settings.Normalize();Check(settings.Language=="de");
        });
        test("English resources preserve every formatting argument",()=>
        {
            Check(L.English.Count>300);
            foreach(var entry in L.English)
            {
                Check(!string.IsNullOrWhiteSpace(entry.Value));
                var original=CompositeFormat.Parse(entry.Key);var translated=CompositeFormat.Parse(entry.Value);
                Check(original.MinimumArgumentCount==translated.MinimumArgumentCount);
                var indexes=System.Text.RegularExpressions.Regex.Matches(entry.Key,@"\{(\d+)[}:,]").Select(m=>m.Groups[1].Value).Order().ToArray();
                var actual=System.Text.RegularExpressions.Regex.Matches(entry.Value,@"\{(\d+)[}:,]").Select(m=>m.Groups[1].Value).Order().ToArray();Check(indexes.SequenceEqual(actual));
            }
        });
        test("English formatting keeps names and numbers intact",()=>In("en",()=>
        {
            Check(L.F($"Helligkeit an {5} Monitoren bestätigt.")=="Brightness confirmed on 5 monitors.");
            Check(L.F($"Profil „{"Rot"}“ gespeichert: {2} Monitore und {3} Fenster.")=="Profile “Rot” saved: 2 monitors and 3 windows.");
        }));
        test("DDC status switches language without probing hardware",()=>
        {
            var monitor=new MonitorDevice{DdcStatus="DDC/CI verbunden · {0} Regler",DdcControlCount=5};
            In("en",()=>Check(monitor.DdcStatus=="DDC/CI connected · 5 controls"));
            In("de",()=>Check(monitor.DdcStatus=="DDC/CI verbunden · 5 Regler"));
            In("en",()=>{var cloned=Store.Clone(monitor);In("de",()=>Check(cloned.DdcStatus=="DDC/CI verbunden · 5 Regler"));});
        });
        test("Language persistence preserves aliases and custom shortcuts",()=>
        {
            string previous=Store.Root;Store.Root=Path.Combine(Path.GetTempPath(),"MonitorPilot-language",Guid.NewGuid().ToString("N"));
            try
            {
                var settings=new AppSettings{Language="en",Aliases=new(){{"a","Helligkeit"}}};settings.Hotkeys[0].Key=0x70;
                Store.Save("settings.json",settings);var loaded=Store.Read<AppSettings>("settings.json")!;loaded.Normalize();
                Check(loaded.Language=="en"&&loaded.Aliases["a"]=="Helligkeit"&&loaded.Hotkeys[0].Key==0x70);
                In("en",()=>Check(new AppController().Name(new(){Name="Rot"})=="Rot"));
            }
            finally{Store.Root=previous;}
        });
        test("Shortcut display follows language without changing key codes",()=>
        {
            var shortcut=AppSettings.Defaults()[0];In("en",()=>Check(HotkeyService.Format(shortcut)=="Ctrl + Alt + Shift + M"));
            In("de",()=>Check(HotkeyService.Format(shortcut)=="Strg + Alt + Umschalt + M"));Check(shortcut.Modifiers==7&&shortcut.Key==0x4D);
        });
        test("Profile validation errors use the selected app language",()=>In("en",()=>
        {
            try{Store.Validate(new());throw new Exception("Invalid profile was accepted");}
            catch(InvalidDataException ex){Check(ex.Message=="The workspace file does not contain a valid display configuration.");}
        }));
    }
}
