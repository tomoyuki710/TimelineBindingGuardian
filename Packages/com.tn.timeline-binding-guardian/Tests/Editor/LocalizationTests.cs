using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace TimelineBindingGuardian
{
    public sealed class LocalizationTests
    {
        private static readonly Regex Placeholder = new Regex(@"(?<!\{)\{(\d+)(?:[,:][^}]*)?\}(?!\})");

        private UiLanguage _saved;

        [SetUp]
        public void SaveLanguage()
        {
            _saved = Lang.Current;
        }

        [TearDown]
        public void RestoreLanguage()
        {
            Lang.Current = _saved;
        }

        [Test]
        public void EveryKey_HasEnglishAndJapanese()
        {
            string[] missing = Lang.Table.Where(p => string.IsNullOrEmpty(p.Value.En) || string.IsNullOrEmpty(p.Value.Ja)).Select(p => p.Key).ToArray();

            Assert.That(missing, Is.Empty);
        }

        [Test]
        public void EveryKey_UsesSamePlaceholdersInBothLanguages()
        {
            string[] mismatched = Lang.Table.Where(p => !PlaceholdersOf(p.Value.En).SequenceEqual(PlaceholdersOf(p.Value.Ja))).Select(p => p.Key).ToArray();

            Assert.That(mismatched, Is.Empty);
        }

        [Test]
        public void EveryKey_FormatsWithoutError()
        {
            foreach (UiLanguage language in new[] { UiLanguage.English, UiLanguage.Japanese })
            {
                Lang.Current = language;
                foreach (string key in Lang.Table.Keys)
                {
                    object[] args = Enumerable.Range(0, 10).Select(i => (object)("a" + i)).ToArray();
                    Assert.DoesNotThrow(() => Lang.T(key, args), key);
                }
            }
        }

        [Test]
        public void T_SwitchesLanguage_AndFallsBackToKey()
        {
            Lang.Current = UiLanguage.Japanese;
            Assert.That(Lang.T("common.language"), Is.EqualTo("言語"));

            Lang.Current = UiLanguage.English;
            Assert.That(Lang.T("common.language"), Is.EqualTo("Language"));
            Assert.That(Lang.T("no.such.key"), Is.EqualTo("no.such.key"));
        }

        [Test]
        public void Note_IsRenderedInCurrentLanguage_AndLegacyTextIsKept()
        {
            string key = Lang.Table.Keys.First(k => PlaceholdersOf(Lang.Table[k].En).Length == 0);
            string stored = Lang.Note(key);

            Lang.Current = UiLanguage.Japanese;
            Assert.That(Lang.RenderNote(stored), Is.EqualTo(Lang.Table[key].Ja));
            Lang.Current = UiLanguage.English;
            Assert.That(Lang.RenderNote(stored), Is.EqualTo(Lang.Table[key].En));

            Assert.That(Lang.RenderNote("Initial observation: 3 binding(s)."), Is.EqualTo("Initial observation: 3 binding(s)."));
            Assert.That(Lang.RenderNote(null), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Resolve_FollowsPreference_ThenOsLanguage()
        {
            CultureInfo japanese = new CultureInfo("ja-JP");
            CultureInfo english = new CultureInfo("en-US");

            Assert.That(Lang.Resolve(LanguagePreference.English, japanese), Is.EqualTo(UiLanguage.English));
            Assert.That(Lang.Resolve(LanguagePreference.Japanese, english), Is.EqualTo(UiLanguage.Japanese));
            Assert.That(Lang.Resolve(LanguagePreference.Auto, japanese), Is.EqualTo(UiLanguage.Japanese));
            Assert.That(Lang.Resolve(LanguagePreference.Auto, english), Is.EqualTo(UiLanguage.English));
            Assert.That(Lang.Resolve(LanguagePreference.Auto, new CultureInfo("fr-FR")), Is.EqualTo(UiLanguage.English));
        }

        private static string[] PlaceholdersOf(string format)
        {
            return Placeholder.Matches(format ?? string.Empty).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(v => v).ToArray();
        }
    }
}
