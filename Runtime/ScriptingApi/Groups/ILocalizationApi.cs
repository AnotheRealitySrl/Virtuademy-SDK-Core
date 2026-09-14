using System;

namespace Virtuademy.ScriptingApi
{
    /// <summary>
    /// The active language, and the authored strings behind it.
    /// </summary>
    public interface ILocalizationApi
    {
        /// <summary>The language currently in use, by name.</summary>
        /// <remarks>Node: <c>Reflectis Localization: Get Localization Data</c>.</remarks>
        string CurrentLanguage { get; }

        /// <summary>The language currently in use, as its code.</summary>
        string CurrentLanguageCode { get; }

        /// <summary>
        /// The translation authored for <paramref name="key"/> in the current language. Returns the
        /// key itself when nothing is authored for it, which is what makes a missing string visible
        /// in the world instead of silently empty.
        /// </summary>
        /// <remarks>Node: <c>Reflectis Localization: Get translation</c>.</remarks>
        string Translate(string key);

        /// <summary>
        /// Switches the language for the whole application, not only for this world. The set of
        /// languages a tenant offers is configured server-side, so a value outside it is ignored.
        /// </summary>
        /// <remarks>Node: <c>Reflectis Localization: Set Language</c>.</remarks>
        void SetLanguage(string language);

        /// <summary>
        /// Raised after the language changes, carrying the new language. Subscribe to re-read
        /// anything a script has already translated and cached.
        /// </summary>
        /// <remarks>Node: <c>Reflectis Localization: On Language Changed</c>.</remarks>
        event Action<string> LanguageChanged;
    }
}
