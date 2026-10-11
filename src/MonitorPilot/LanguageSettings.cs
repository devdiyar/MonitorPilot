using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;

namespace MonitorPilot;
internal sealed partial class MainWindow
{
    private sealed record LanguageOption(string Code,string Name){public override string ToString()=>Name;}
    private UIElement LanguageSelector()
    {
        var options=new[]{new LanguageOption("de","Deutsch"),new LanguageOption("en","English")};
        var choice=new ComboBox{ItemsSource=options,SelectedItem=options.First(x=>x.Code==c.Settings.Language),MinWidth=180,HorizontalAlignment=HorizontalAlignment.Left};
        AutomationProperties.SetAutomationId(choice,"AppLanguage");AutomationProperties.SetName(choice,L.T("Sprache"));
        choice.SelectionChanged+=(_,_)=>{if(choice.SelectedItem is LanguageOption selected)SetLanguage(selected.Code);};
        return Theme.Panel(Theme.Stack(Theme.Label(L.T("Sprache"),17,null,true),Theme.Label(L.T("Sprache sofort wechseln. Deine Auswahl wird gespeichert."),13,Theme.Muted),choice));
    }
    private void SetLanguage(string language)
    {
        string old=c.Settings.Language;language=L.Normalize(language);if(old==language)return;
        try{c.Settings.Language=language;c.SaveSettings();}
        catch(Exception ex){c.Settings.Language=old;c.Report(ex.Message,true);Render();return;}
        L.SetLanguage(language);
    }
    private void LanguageChanged()
    {
        // The controller owns pending slider targets; rebuilding text never enqueues
        // hardware writes. Reuse controller state rather than querying monitors again.
        trayQuick?.Close();
        if(pageScroll!=null)pageScroll.Content=null;
        foreach(var element in new FrameworkElement[]{heading,subtitle,hotkeyStatus,status})
        {
            if(element.Parent is Panel panel)panel.Children.Remove(element);
            else if(element.Parent is Decorator decorator)decorator.Child=null;
        }
        Content=null;nav.Clear();appearanceButtons.Clear();
        Language=XmlLanguage.GetLanguage(L.Culture.Name);Title=L.T("MonitorPilot · Dein Monitor-Arbeitsplatz");
        hotkeyStatus.Text=hotkeys==null?L.T("Tastenkürzel werden geprüft"):string.IsNullOrEmpty(hotkeyProblem)?L.T("●  Globale Tastenkürzel aktiv"):L.T("⚠  Tastenkürzel prüfen");
        BuildShell();
        if(tray!=null){tray.Text=L.T("MonitorPilot · Monitore steuern");BuildTrayMenu();}
        if(!c.Busy&&!c.Live.IsRunning)c.Report(L.T("Sprache gespeichert."));
    }
}
