using System;

using System.Collections.Generic;

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
        /// The language in force before the last change, empty until one happens. The node that
        /// reports a language change publishes it beside the new one, so a world can say what it is
        /// switching from.
        /// </summary>
        string PreviousLanguage { get; }

        /// <summary>The same, as the code rather than the display name.</summary>
        string PreviousLanguageCode { get; }

        /// <summary>Every language the host offers, as display names.</summary>
        /// <remarks>Node: <c>Reflectis Localization: Get Localization Data</c>.</remarks>
        List<string> AvailableLanguages { get; }

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
    
        /// <summary>
        /// Whether the host application has a localization system at all. False in a world opened
        /// straight from the editor, where <see cref="Translate"/> hands the key back unchanged.
        /// </summary>
        /// <remarks>
        /// Node: <c>Reflectis Localization: On Language Changed</c>, which subscribes only when this
        /// is true.
        /// </remarks>
        bool IsAvailable { get; }
}
}
