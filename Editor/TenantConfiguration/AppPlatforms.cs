using System;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// The platform tags of an app key (<c>cap_platform</c> in the Platform DB), as <c>config-public</c> returns them in
    /// <c>app.platform</c>. Each kind of project accepts only the keys made for it (ADR 0037): an environment project the
    /// <see cref="CreatorKit"/> ones, the Virtuademy application the platform of the build it configures, an external app
    /// <see cref="ExternalApp"/>. The tag is a free string in the database, so the values below are the ones the SDK acts on,
    /// not a closed list.
    /// </summary>
    public static class AppPlatforms
    {
        public const string CreatorKit = "CreatorKit";
        public const string WebGL = "WebGL";
        public const string Mobile = "Mobile";
        public const string VR = "VR";
        public const string ExternalApp = "ExternalApp";

        /// <summary>Trimmed, ordinal and case-insensitive: the tag is typed by an operator.</summary>
        public static bool Matches(string actual, string expected) =>
            string.Equals(actual?.Trim(), expected?.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>A short label for a message ("a WebGL app"); the raw value when the tag is not one the SDK knows.</summary>
        public static string Describe(string platform)
        {
            string value = platform?.Trim();
            if (string.IsNullOrEmpty(value))
                return "an app with no platform";

            if (Matches(value, CreatorKit)) return "a Creator Kit app";
            if (Matches(value, WebGL)) return "a WebGL app";
            if (Matches(value, Mobile)) return "a mobile app";
            if (Matches(value, VR)) return "a VR app";
            if (Matches(value, ExternalApp)) return "an external app";
            if (Matches(value, "BO")) return "a Back Office app (BO)";
            if (Matches(value, "BE")) return "a Back End app (BE)";
            if (Matches(value, "SPACS")) return "a SPACS app";
            if (Matches(value, "Management-App")) return "a management app";
            if (Matches(value, "eCommerce")) return "an eCommerce app";

            return value;
        }
    }

    /// <summary>
    /// The app selected for a tenant switch is made for another platform than the project takes
    /// (<see cref="TenantSwitch.ReadTenant"/>). Thrown rather than reported with a null, because the application's
    /// configurator would write the credentials on a null all the same. The message says what to do.
    /// </summary>
    public sealed class AppPlatformMismatchException : Exception
    {
        /// <summary>The platform the project takes.</summary>
        public string Expected { get; }

        /// <summary>The platform the selected app has.</summary>
        public string Found { get; }

        public AppPlatformMismatchException(string expected, string found)
            : base($"The selected app is for {AppPlatforms.Describe(found)} (platform '{found}'), and this project takes {AppPlatforms.Describe(expected)} " +
                   $"(platform '{expected}'). Select the configuration file of an app made for it. Nothing was changed.")
        {
            Expected = expected;
            Found = found;
        }
    }
}
