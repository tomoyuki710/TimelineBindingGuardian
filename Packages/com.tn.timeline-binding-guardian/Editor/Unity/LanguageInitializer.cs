using UnityEditor;

namespace TimelineBindingGuardian
{
    /// <summary>
    /// Editor の起動・Domain Reload 時に、設定の表示言語を反映する（Console の Warning など、ウィンドウを開く前に出る文言のため）。
    /// </summary>
    [InitializeOnLoad]
    internal static class LanguageInitializer
    {
        static LanguageInitializer()
        {
            GuardianSettings.Instance.ApplyLanguage();
        }
    }
}
