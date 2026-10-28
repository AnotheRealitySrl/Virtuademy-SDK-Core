using Virtuademy.SDK.Core.ApiSystem;
using Virtuademy.SDK.Http;

using System.Threading.Tasks;

using UnityEditor;

using UnityEngine;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// The tenant switch itself: read the tenant the selected app belongs to, and write what a
    /// build needs to reach it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to live entirely in the platform's own application, as the base class every
    /// configurator derived from. Nothing in it is application-specific — a project that has chosen
    /// an app and an environment needs exactly these two assets, because every client reads them at
    /// runtime — but a project without that base class got none of it. That is every external
    /// application: the window offered a tenant, the tenant was selected, and nothing was written.
    /// </para>
    /// <para>
    /// An application with its own configurator still runs it, and it calls into here rather than
    /// repeating the flow, so there is one implementation of what a switch means.
    /// </para>
    /// </remarks>
    public static class TenantSwitch
    {
        /// <summary>
        /// Everything a project without an application-specific configurator needs: the tenant,
        /// then the two generated assets.
        /// </summary>
        public static async Task Apply(AppConfigurationSettings settings)
        {
            Tenant tenant = await ReadTenant(settings);

            if (tenant == null)
            {
                return;
            }

            await PlatformConfigWriter.WriteFor(settings, tenant);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Says which URI scheme the platform will launch this application with.
        /// </summary>
        /// <remarks>
        /// Reported, not recorded: it is a function of the credential in the app config, so the
        /// Android build works it out for itself and there is no copy to go stale. Said out loud
        /// here because the switch is the moment a developer is looking, and because a manifest
        /// written by hand — for a project that does not use the build step — needs the value.
        /// </remarks>
        private static void ReportLaunchScheme(AppConfigurationSettings settings)
        {
            string scheme = LaunchScheme.Derive(settings.SelectedConfig?.Credential?.AppId);

            if (!LaunchScheme.IsValid(scheme))
            {
                Debug.LogWarning("[TenantSwitch] No launch scheme for this app: the app config "
                                 + "carries no credential to derive one from, so the Android build "
                                 + "will claim none and the platform cannot launch this application.");
                return;
            }

            Debug.Log($"[TenantSwitch] Launch scheme: {scheme}://. The Android build claims it in "
                      + "the manifest, and the platform derives the same value from the app id the "
                      + "experience is bound to.");
        }

        /// <summary>
        /// Fetches the tenant and the app's custom configuration for the selected app, and records
        /// both on the settings asset.
        /// </summary>
        /// <remarks>
        /// The custom configuration is cached rather than used: what is in it is per-application —
        /// the platform's own configurator reads a product name and a UI theme out of it — so this
        /// only makes it available. A failure to fetch it is not fatal, because a project that does
        /// not read it should still get a tenant.
        /// <para>
        /// The app's platform is checked first (<see cref="CheckPlatformAsync"/>), before anything is read or recorded.
        /// </para>
        /// </remarks>
        /// <returns>The tenant, or null when it could not be read — in which case nothing is written.</returns>
        /// <exception cref="AppPlatformMismatchException">The selected app is made for another platform than this project
        /// takes. Not a null return: the configurator of the application does not stop on null, and would write the
        /// credentials anyway.</exception>
        public static async Task<Tenant> ReadTenant(AppConfigurationSettings settings)
        {
            AppIdentification appConfig = settings.SelectedConfig;

            await CheckPlatformAsync(settings);

            ApiResponse<Tenant> tenantData = await TenantConfigurationClient.GetTenantData(appConfig);

            if (!tenantData.IsSuccess)
            {
                Debug.LogError($"[TenantSwitch] Failed to get tenant data: {tenantData.ReasonPhrase}");
                return null;
            }

            ApiResponse<Newtonsoft.Json.Linq.JObject> customConfig =
                await TenantConfigurationClient.GetAppCustomConfig(appConfig);

            if (!customConfig.IsSuccess)
            {
                Debug.LogError($"[TenantSwitch] Failed to get app custom config: {customConfig.ReasonPhrase}");
            }

            settings.CachedTenant = tenantData.Content;
            settings.CachedAppConfigJson = customConfig.IsSuccess ? customConfig.Content?.ToString() : null;

            ReportLaunchScheme(settings);

            EditorUtility.SetDirty(settings);

            return tenantData.Content;
        }

        /// <summary>
        /// Refuses an app made for another platform than this project takes (meta-repo ADR 0037, "The app key's platform"):
        /// the Virtuademy application takes the platform of the build it configures (<see cref="AppConfigurationSettings.AppPlatform"/>),
        /// an external app <see cref="AppPlatforms.ExternalApp"/>, an environment project <see cref="AppPlatforms.CreatorKit"/>.
        /// An application whose settings name no platform, a Configuration-API that does not send the field and a call that
        /// fails are warned about and accepted: the check could not run, which is not a mismatch.
        /// </summary>
        private static async Task CheckPlatformAsync(AppConfigurationSettings settings)
        {
#if VIRTUADEMY_APPLICATION
            string expected = settings.AppPlatform;
            if (string.IsNullOrWhiteSpace(expected))
            {
                Debug.LogWarning($"[TenantSwitch] {settings.name} names no app platform, so the check that the selected app is made for this build could not run.");
                return;
            }
#elif VIRTUADEMY_SDK_ENVIRONMENTS
            string expected = AppPlatforms.CreatorKit;
#else
            string expected = AppPlatforms.ExternalApp;
#endif

            ApiResponse<TenantPublicConfig> publicConfig = await TenantConfigurationClient.GetTenantPublicConfig(settings.SelectedConfig);
            if (!publicConfig.IsSuccess)
            {
                Debug.LogWarning($"[TenantSwitch] The app's platform could not be read ({publicConfig.ReasonPhrase}), so the check that it is made for this project could not run.");
                return;
            }

            string found = publicConfig.Content?.App?.Platform;
            if (found == null)
            {
                Debug.LogWarning("[TenantSwitch] The platform did not say which kind of app the selected one is (a Configuration-API older than 2026.7.0), " +
                                 "so the check that it is made for this project could not run.");
                return;
            }

            if (!AppPlatforms.Matches(found, expected))
                throw new AppPlatformMismatchException(expected, found);
        }
    }
}
