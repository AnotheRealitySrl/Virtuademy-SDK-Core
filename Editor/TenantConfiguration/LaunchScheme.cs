using System;
using System.Text.RegularExpressions;

using Newtonsoft.Json.Linq;

using Virtuademy.SDK.Core.ApiSystem;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// The URI scheme the platform uses to launch this application, worked out from what the
    /// developer was given rather than typed by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The platform launches an external application by opening <c>scheme://…?authSessionHash=…</c>
    /// through an <c>ACTION_VIEW</c> intent. For that to reach anything, the application's manifest
    /// has to claim the same scheme — and until now the two halves were typed separately, the
    /// platform half into an experience's config in the back office and the application half into a
    /// hand-written <c>AndroidManifest.xml</c>, with nothing checking that they agreed. A
    /// mismatch is silent: the intent simply finds no one.
    /// </para>
    /// <para>
    /// <b>Derived from the app id, so there is nothing to agree on.</b> An external developer
    /// receives one file, the app config, and the tenant switch computes the scheme from the
    /// credential in it — the same value the platform can compute for the same app. It can still be
    /// overridden, because an app that already ships with a scheme cannot be asked to change the
    /// one its users have installed, and because the platform will eventually carry the value
    /// itself: when the app's configuration reports one, that wins outright.
    /// </para>
    /// </remarks>
    public static class LaunchScheme
    {
        /// <summary>
        /// Keys searched in the app's configuration, in order. The first two are where a
        /// platform-carried scheme would sit if the back office starts recording one; until then
        /// nothing answers and the derivation below applies.
        /// </summary>
        private static readonly string[] config_keys = { "androidAppScheme", "launchScheme" };

        /// <summary>
        /// Android accepts rather more than this in a scheme, but a scheme that survives being
        /// typed, logged and pasted into a back-office field is worth more than one that is merely
        /// legal.
        /// </summary>
        private static readonly Regex valid = new("^[a-z][a-z0-9.+-]*$", RegexOptions.Compiled);

        /// <summary>
        /// The scheme for an application, or null when there is nothing to derive one from.
        /// </summary>
        /// <param name="appConfig">The selected app config. Its credential is what the scheme is derived from.</param>
        /// <param name="appCustomConfig">The app's configuration as the platform reports it, or null.</param>
        /// <param name="overrideScheme">What the project has pinned, if anything. Wins over the derivation, loses to the platform.</param>
        public static string For(AppIdentification appConfig,
                                 JObject appCustomConfig,
                                 string overrideScheme = null)
        {
            string reported = FromConfig(appCustomConfig);

            if (!string.IsNullOrEmpty(reported))
            {
                return reported;
            }

            if (!string.IsNullOrWhiteSpace(overrideScheme))
            {
                return overrideScheme.Trim().ToLowerInvariant();
            }

            return Derive(appConfig?.Credential?.AppId);
        }

        /// <summary>
        /// <c>virtuademy-</c> plus the first block of the app id: unique per registered app,
        /// stable across environments of the same app, and short enough to read back over a call.
        /// </summary>
        public static string Derive(Guid? appId)
        {
            if (appId == null || appId == Guid.Empty)
            {
                return null;
            }

            string firstBlock = appId.Value.ToString("D").Split('-')[0];

            return $"virtuademy-{firstBlock}".ToLowerInvariant();
        }

        /// <summary>Whether a scheme can be put in a manifest as it stands.</summary>
        public static bool IsValid(string scheme)
            => !string.IsNullOrEmpty(scheme) && valid.IsMatch(scheme);

        private static string FromConfig(JObject appCustomConfig)
        {
            if (appCustomConfig == null)
            {
                return null;
            }

            foreach (string key in config_keys)
            {
                // Looked up anywhere in the document rather than at a fixed path: the app config is
                // free-form JSON whose shape differs per app, and the platform has not settled on
                // where a launch scheme would live.
                JToken found = appCustomConfig.SelectToken($"$..{key}");
                string value = found?.Value<string>();

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim().ToLowerInvariant();
                }
            }

            return null;
        }
    }
}
