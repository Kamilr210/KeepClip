using System.Text.RegularExpressions;

namespace KeepClip.Tests.Infrastructure;

public class StringsTests
{
    public StringsTests() => TestSettings.Reset();

    [Fact]
    public void Every_ui_language_has_a_table()
        => Assert.Equal(Config.UiLanguages.OrderBy(l => l), Strings.Languages.OrderBy(l => l));

    [Fact]
    public void Every_language_has_the_same_keys()
    {
        var reference = Strings.Entries("pl").Keys.OrderBy(k => k).ToList();

        foreach (var lang in Strings.Languages)
        {
            var keys = Strings.Entries(lang).Keys.OrderBy(k => k).ToList();
            Assert.True(reference.SequenceEqual(keys),
                $"Język {lang}: brakuje {string.Join(", ", reference.Except(keys))}; nadmiarowe {string.Join(", ", keys.Except(reference))}");
        }
    }

    [Fact]
    public void No_translation_is_empty()
    {
        foreach (var lang in Strings.Languages)
            foreach (var (key, value) in Strings.Entries(lang))
                Assert.False(string.IsNullOrWhiteSpace(value), $"{lang}: pusty tekst dla {key}");
    }

    [Fact]
    public void Placeholders_match_across_languages()
    {
        static string Slots(string text) =>
            string.Join(",", Regex.Matches(text, @"\{(\d+)\}").Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x));

        foreach (var (key, plText) in Strings.Entries("pl"))
            foreach (var lang in Strings.Languages)
                Assert.True(Slots(plText) == Slots(Strings.Entries(lang)[key]),
                    $"{key}: pl ma {{{Slots(plText)}}}, {lang} ma {{{Slots(Strings.Entries(lang)[key])}}}");
    }

    [Fact]
    public void Every_key_used_in_source_exists_in_every_language()
    {
        var sourceDir = Path.Combine(Config.AppRoot, "KeepClip");
        Assert.True(Directory.Exists(sourceDir), $"Brak katalogu źródeł {sourceDir}; test musi biec z repozytorium.");

        var used = Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"Strings\.Get\(\s*""([^""]+)""").Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();

        Assert.NotEmpty(used);
        foreach (var lang in Strings.Languages)
        {
            var missing = used.Where(k => !Strings.Entries(lang).ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, $"Język {lang} nie ma kluczy: {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void Get_follows_the_saved_language_and_falls_back_to_default()
    {
        Settings.SetLanguage("pl");
        Assert.Equal("Klip nie istnieje.", Strings.Get("clip.notFound"));

        Settings.SetLanguage("en");
        Assert.Equal("The clip does not exist.", Strings.Get("clip.notFound"));
    }

    [Fact]
    public void Get_returns_the_key_itself_when_nothing_matches()
        => Assert.Equal("brak.takiego.klucza", Strings.Get("brak.takiego.klucza"));

    [Fact]
    public void Get_formats_arguments()
    {
        Settings.SetLanguage("pl");

        var text = Strings.Get("folder.duplicate", "Ulubione");

        Assert.Contains("Ulubione", text);
        Assert.DoesNotContain("{0}", text);
    }
}
