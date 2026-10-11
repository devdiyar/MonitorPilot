using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace MonitorPilot;

// German source strings are the keys. User-entered names and persistent action keys
// are never translated. Formatting happens after translation to preserve arguments.
internal static class L
{
    private static readonly Dictionary<string,string> english=ReadEnglish();
    public static string Language {get;private set;}="de";
    public static CultureInfo Culture=>CultureInfo.GetCultureInfo(Language=="en"?"en-US":"de-DE");
    public static event Action? Changed;
    internal static IReadOnlyDictionary<string,string> English=>english;
    public static string Normalize(string? language)=>language=="en"?"en":"de";
    public static void SetLanguage(string? language)
    {
        var next=Normalize(language);if(next==Language)return;Language=next;Changed?.Invoke();
    }
    public static string T(string text)=>Language=="en"&&english.TryGetValue(text,out var translated)?translated:text;
    public static string F(FormattableString text)=>string.Format(Culture,T(text.Format),text.GetArguments());
    public static string Format(string template,params object?[] args)=>string.Format(Culture,T(template),args);
    private static Dictionary<string,string> ReadEnglish()
    {
        using var stream=typeof(L).Assembly.GetManifestResourceStream("MonitorPilot.UI.Strings.en.json")??throw new InvalidDataException("Missing English language resource.");
        return JsonSerializer.Deserialize<Dictionary<string,string>>(stream)??throw new InvalidDataException("Empty English language resource.");
    }
}
