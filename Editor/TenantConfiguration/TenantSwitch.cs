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
        /// Fetches the tenant and the app's custom configuration for the selected app, and records
        /// both on the settings asset.
        /// </summary>
        /// <remarks>
        /// The custom configuration is cached rather than used: what is in it is per-application —
        /// the platform's own configurator reads a product name and a UI theme out of it — so this
        /// only makes it available. A failure to fetch it is not fatal, because a project that does
        /// not read it should still get a tenant.
        /// </remarks>
        /// <returns>The tenant, or null when it could not be read — in which case nothing is written.</returns>
        public static async Task<Tenant> ReadTenant(AppConfigurationSettings settings)
        {
            AppIdentification appConfig = settings.SelectedConfig;

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
            EditorUtility.SetDirty(settings);

            return tenantData.Content;
        }
    }
}
