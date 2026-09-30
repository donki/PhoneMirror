using System.Reflection;
using System.Text.RegularExpressions;
using PhoneMirror.Localization;

namespace PhoneMirror.Tests;

/// <summary>Los textos: las mismas claves en los dos idiomas, sin huecos y con los mismos {n}.</summary>
public partial class LocTests
{
    private static Dictionary<string, string> Table(string name) =>
        (Dictionary<string, string>)typeof(Loc).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    private static void Use(string language)
    {
        if (Loc.Language != language)
            Loc.Toggle();
        Assert.Equal(language, Loc.Language);
    }

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex Placeholder();

    [Fact]
    public void SpanishAndEnglishHaveTheSameKeys()
    {
        var en = Table("English").Keys.Order().ToList();
        var es = Table("Spanish").Keys.Order().ToList();

        Assert.Equal(en, es);
        Assert.True(en.Count > 50);
    }

    [Fact]
    public void NoTextIsEmpty()
    {
        Assert.All(Table("English"), kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), kv.Key));
        Assert.All(Table("Spanish"), kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), kv.Key));
    }

    [Fact]
    public void PlaceholdersAreTheSameInBothLanguages()
    {
        var es = Table("Spanish");
        foreach (var (key, english) in Table("English"))
        {
            var a = Placeholder().Matches(english).Select(m => m.Value).Distinct().Order();
            var b = Placeholder().Matches(es[key]).Select(m => m.Value).Distinct().Order();
            Assert.True(a.SequenceEqual(b), key);
        }
    }

    [Fact]
    public void NoOtherBraces_SoFormatNeverThrows()
    {
        foreach (var table in new[] { Table("English"), Table("Spanish") })
        {
            foreach (var (key, text) in table)
            {
                var args = Enumerable.Range(0, 5).Select(i => (object)$"<{i}>").ToArray();
                var formatted = string.Format(text, args);
                Assert.DoesNotContain("{", formatted);
                Assert.False(formatted.Contains('}'), key);
            }
        }
    }

    [Fact]
    public void SpanishTexts_SayMovilNotTelefono_AndHaveAccents()
    {
        var es = Table("Spanish");
        Assert.DoesNotContain(es.Values, v => v.Contains("aparato", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(es.Values, v => v.Contains("móvil"));
        Assert.DoesNotContain(es.Values, v => v.Contains("movil"));
    }

    [Fact]
    public void Get_Toggle_Format_AndFallbacks()
    {
        var original = Loc.Language;
        try
        {
            var changes = 0;
            void OnChanged() => changes++;
            Loc.LanguageChanged += OnChanged;

            Use("es");
            Assert.Equal("Cerrar", Loc.Get("Close"));
            Assert.Equal("Conectando con Pixel 7…", Loc.Format("Connecting", "Pixel 7"));
            Assert.Equal("Pixel 7 · 1080×2400", Loc.Format("Connected", "Pixel 7", 1080, 2400));

            Use("en");
            Assert.Equal("Close", Loc.Get("Close"));
            Assert.Equal("Could not send a.apk: boom", Loc.Format("DropFailed", "a.apk", "boom"));

            Assert.Equal(string.Empty, Loc.Get("NoExisteEstaClave"));
            Use("es");
            Assert.Equal(string.Empty, Loc.Get("NoExisteEstaClave"));

            Loc.LanguageChanged -= OnChanged;
            Assert.True(changes >= 1);
        }
        finally
        {
            Use(original);
        }
    }

    [Fact]
    public void Get_SpanishFallsBackToEnglish_WhenAKeyIsMissing()
    {
        var original = Loc.Language;
        var es = Table("Spanish");
        const string key = "__solo_en_ingles__";
        Table("English")[key] = "only in English";
        try
        {
            Use("es");
            Assert.False(es.ContainsKey(key));
            Assert.Equal("only in English", Loc.Get(key));
        }
        finally
        {
            Table("English").Remove(key);
            Use(original);
        }
    }

    [Fact]
    public void TExtension_ProvidesTheText()
    {
        var original = Loc.Language;
        try
        {
            Use("en");
            Assert.Equal("Close", new TExtension("Close").ProvideValue(null!));
            Assert.Equal(string.Empty, new TExtension().ProvideValue(null!));
            Assert.Equal("Close", new TExtension { Key = "Close" }.ProvideValue(null!));
        }
        finally
        {
            Use(original);
        }
    }
}
