using Virtuademy.SDK.Core.ApiSystem;
using Virtuademy.SDK.Http;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using UnityEditor;

using UnityEngine;


using Virtuademy.SDK.Core.Utilities;

using SPACS.Utilities;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// Writes the two generated configuration assets at tenant switch: the endpoint table the build
    /// falls back to, and the credential it signs pre-login calls with. See ADR 0025 in the
    /// meta-repo.
    /// <para>
    /// This is the replacement for stamping an <c>AppIdentification</c> into every API system asset,
    /// which is how 18 committed secrets and 23 divergent copies of the same URLs came to exist.
    /// </para>
    /// </summary>
    public static class PlatformConfigWriter
    {
        /// <summary>
        /// A folder named <c>Resources</c> is not cosmetic: the assets are read through
        /// <c>Resources.Load</c> so that the editor and a player build resolve them the same way,
        /// and an asset outside one is simply absent from the build. See <c>PlatformConfig</c>.
        /// </summary>
        private const string config_folder_path = "Assets/Virtuademy/PlatformConfig/Resources";

        /// <summary>
        /// Canonical API type → the version recorded for it in the tenant's config.
        /// <para>
        /// This map exists only because <c>ApiEndpoint</c> carries a base URL but no version, while
        /// <c>TenantConfig</c> carries both for four hardcoded APIs. Endpoints are enumerated from
        /// the type-keyed <c>api-endpoints</c> response — adding an API needs no change here — but
        /// its version falls back to this table, so a new API arrives with a null version until the
        /// field is added to the DTO. Tracked as an open question in ADR 0025.
        /// </para>
        /// </summary>
        private static string VersionFor(string apiType, TenantConfig config, string configurationApiVersion)
        {
            if (string.IsNullOrEmpty(apiType))
            {
                return null;
            }

            // Not one of the tenant's four: the Configuration API is the one an application is
            // told about before it can ask the platform anything, so its version comes from the
            // app config the developer was handed.
            if (string.Equals(apiType, "Configuration", StringComparison.OrdinalIgnoreCase))
            {
                return configurationApiVersion;
            }

            if (config == null)
            {
                return null;
            }

            if (string.Equals(apiType, "Application", StringComparison.OrdinalIgnoreCase)) return config.ApplicationApiVersion;
            if (string.Equals(apiType, "Realtime", StringComparison.OrdinalIgnoreCase)) return config.RealtimeApiVersion;
            if (string.Equals(apiType, "AI", StringComparison.OrdinalIgnoreCase)) return config.AIApiVersion;
            if (string.Equals(apiType, "Profile", StringComparison.OrdinalIgnoreCase)) return config.ProfileApiVersion;

            return null;
        }

        /// <summary>
        /// Fetches the endpoint table for the selected app and writes both generated assets.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The pair is written together because they are one answer to one question — which
        /// platform this project talks to, and what it signs its pre-login calls with — and a
        /// project holding one without the other reaches an endpoint it cannot authenticate to.
        /// </para>
        /// <para>
        /// A failed endpoint fetch is a warning rather than a failure: every type it would have
        /// reported keeps whatever the asset already held, so one bad request cannot point a build
        /// at nothing. The <c>Configuration</c> entry is written regardless, because it comes from
        /// the app config rather than from the fetch — which is what lets a project work on an
        /// environment that does not serve discovery at all.
        /// </para>
        /// </remarks>
        public static async Task WriteFor(AppConfigurationSettings settings, Tenant tenant)
        {
            AppIdentification appConfig = settings.SelectedConfig;

            ApiResponse<List<ApiEndpoint>> endpoints =
                await TenantConfigurationClient.GetApiEndpoints(appConfig);

            if (!endpoints.IsSuccess)
            {
                Debug.LogWarning($"[PlatformConfigWriter] Could not fetch API endpoints " +
                                 $"({endpoints.StatusCode} {endpoints.ReasonPhrase}); the types it " +
                                 "would have reported keep whatever the asset already held. A 401 " +
                                 "here usually means the environment does not serve " +
                                 "manage/apps/api-endpoints yet — the request falls through to the " +
                                 "id route, which rejects it before it can fail to bind. The " +
                                 "Configuration entry is written either way, so the project can " +
                                 "still reach the platform and read its tenant.");
            }

            WriteEndpoints(tenant,
                           endpoints.IsSuccess ? endpoints.Content : null,
                           $"{settings.SelectedApp} / {settings.SelectedEnv}",
                           appConfig);

            WriteCredentials(appConfig?.Credential);
        }

        /// <summary>
        /// Records what the platform reported for this app, into the project's
        /// <see cref="PlatformEndpoints"/> asset — found if it exists, created once if it does not.
        /// </summary>
        /// <param name="tenant">Source of the API versions, until the endpoint DTO carries them.</param>
        /// <param name="apiEndpoints">The <c>api-endpoints</c> response, or null when the fetch failed. Its <c>BaseUrls</c> are already ordered by the server, so the first is taken.</param>
        /// <param name="generatedFrom">App and environment, recorded in the asset so a diff says where the values came from.</param>
        /// <param name="appConfig">
        /// The app config this switch was driven from. It supplies the <c>Configuration</c> entry,
        /// which discovery structurally cannot: asking the platform where the Configuration API
        /// lives requires already knowing. A project holding only that one entry still works — it
        /// reads its tenant, and the tenant carries the other four addresses.
        /// </param>
        /// <returns>The asset, or null when nothing was written — in which case any existing content is left untouched.</returns>
        public static PlatformEndpoints WriteEndpoints(Tenant tenant,
                                                       IReadOnlyList<ApiEndpoint> apiEndpoints,
                                                       string generatedFrom,
                                                       AppIdentification appConfig = null)
        {
            List<PlatformEndpoint> entries = (apiEndpoints ?? Array.Empty<ApiEndpoint>())
                .Where(e => e != null && !string.IsNullOrEmpty(e.Type) && e.BaseUrls != null && e.BaseUrls.Count > 0)
                .Select(e => new PlatformEndpoint(e.Type, e.BaseUrls[0],
                                                  VersionFor(e.Type, tenant?.Config, appConfig?.ApiVersion)))
                .ToList();

            // The address the switch itself just talked to, recorded because nothing else can
            // record it. Discovery is the authority for every other type; for this one it is
            // circular, and on an environment that does not serve discovery at all this is the
            // only entry there is.
            if (!string.IsNullOrEmpty(appConfig?.ApiBaseUrl)
                && !entries.Any(e => string.Equals(e.ApiType, "Configuration", StringComparison.OrdinalIgnoreCase)))
            {
                entries.Insert(0, new PlatformEndpoint("Configuration",
                                                       appConfig.ApiBaseUrl,
                                                       appConfig.ApiVersion));
            }

            if (entries.Count == 0)
            {
                Debug.LogWarning("[PlatformConfigWriter] No API endpoints to write — the fetch returned none " +
                                 "and the app config carries no base URL. Leaving the existing asset as it was: " +
                                 "a failed request must not point a build at nothing.");
                return null;
            }

            // A type that appears twice makes the table ambiguous for every consumer, because
            // resolution is keyed on type alone (ADR 0024) and both TryGet here and
            // TenantConfigurationClient.TryGetBaseUrl take the first match. Recording it silently
            // would leave a build resolving to whichever the platform happened to list first, with
            // nothing ever reporting the choice — so it is said out loud at generation time, which
            // is the only moment somebody is looking.
            foreach (IGrouping<string, PlatformEndpoint> duplicate in entries
                         .GroupBy(e => e.ApiType, StringComparer.OrdinalIgnoreCase)
                         .Where(g => g.Count() > 1))
            {
                Debug.LogWarning($"[PlatformConfigWriter] API type '{duplicate.Key}' was reported " +
                                 $"{duplicate.Count()} times: {string.Join(", ", duplicate.Select(e => e.BaseUrl))}. " +
                                 "Resolution keys on the type alone, so a consumer asking for it gets the first " +
                                 "of these and no warning. Either the registrations need distinct cpi_type values " +
                                 "or the contract needs a tiebreak.");
            }

            PlatformEndpoints asset = FindOrCreate<PlatformEndpoints>(nameof(PlatformEndpoints) + ".asset");

            // What is written never holds fewer types than what was there. The asset is one whole
            // list, so writing only what this run learned would turn a partial answer — a failed
            // discovery that still knows the Configuration address — into a build that had lost
            // four addresses it used to have. A type this run did report replaces the old one.
            foreach (PlatformEndpoint kept in asset.Endpoints)
            {
                if (kept != null
                    && !string.IsNullOrEmpty(kept.ApiType)
                    && !entries.Any(e => string.Equals(e.ApiType, kept.ApiType, StringComparison.OrdinalIgnoreCase)))
                {
                    entries.Add(kept);
                }
            }

            if (!asset.Write(generatedFrom, entries))
            {
                Debug.LogWarning("[PlatformConfigWriter] Every endpoint in the response was unusable (no type or no base URL). " +
                                 $"{nameof(PlatformEndpoints)} left unchanged.");
                return null;
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            PlatformConfig.InvalidateCache();

            Debug.Log($"[PlatformConfigWriter] Wrote {entries.Count} endpoint(s) from {generatedFrom}: " +
                      string.Join(", ", entries.Select(e => e.ApiType)));

            return asset;
        }

        /// <summary>
        /// Records the HMAC credential into the project's <see cref="PlatformCredentials"/> asset —
        /// found if it exists, created once if it does not.
        /// </summary>
        /// <remarks>
        /// The asset it writes is a secret and belongs in <c>.gitignore</c>. This method does not
        /// enforce that, because a tool that silently edited a repository's ignore rules would be
        /// worse than one that does not; the entry is part of the setup, and the ADR records it.
        /// </remarks>
        public static PlatformCredentials WriteCredentials(HmacCredential credential)
        {
            PlatformCredentials asset = FindOrCreate<PlatformCredentials>(nameof(PlatformCredentials) + ".asset");

            if (!asset.Write(credential))
            {
                Debug.LogWarning("[PlatformConfigWriter] Incomplete credential (missing app id or secret). " +
                                 $"{nameof(PlatformCredentials)} left unchanged.");
                return null;
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            PlatformConfig.InvalidateCache();

            return asset;
        }

        /// <summary>
        /// The project's asset of type <typeparamref name="T"/>, created at the default path only if
        /// the project has none. Modifying what is already there — rather than adding a second one —
        /// is what keeps a project from accumulating shadowed copies, and it lets a project place
        /// the asset wherever it likes.
        /// </summary>
        /// <remarks>
        /// When several exist the first is taken and the rest are reported. Picking silently would
        /// hide a real problem: two endpoint assets mean half the project reads the wrong one, and
        /// nothing else would ever say so.
        /// </remarks>
        private static T FindOrCreate<T>(string fileName) where T : ScriptableObject
        {
            string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);

            List<T> found = guids
                .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a => a != null)
                .ToList();

            if (found.Count > 1)
            {
                Debug.LogWarning($"[PlatformConfigWriter] {found.Count} {typeof(T).Name} assets in this project; " +
                                 $"writing the first one ({AssetDatabase.GetAssetPath(found[0])}). " +
                                 "The others are shadowed and should be deleted: " +
                                 string.Join(", ", found.Skip(1).Select(AssetDatabase.GetAssetPath)));
            }

            if (found.Count > 0)
            {
                WarnIfNotLoadable(found[0]);
                return found[0];
            }

            EnsureFolderExists(config_folder_path);

            T created = ScriptableObject.CreateInstance<T>();
            string assetPath = $"{config_folder_path}/{fileName}";
            AssetDatabase.CreateAsset(created, assetPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[PlatformConfigWriter] Created {assetPath}.");

            return created;
        }

        /// <summary>
        /// An asset outside a <c>Resources</c> folder resolves in the editor and is missing from
        /// the build, which is the least detectable shape a configuration fault can take: every
        /// consumer falls back silently to what it already had, and the fallback is documented
        /// behaviour. So it is reported here, where somebody is looking.
        /// </summary>
        private static void WarnIfNotLoadable<T>(T asset) where T : ScriptableObject
        {
            string path = AssetDatabase.GetAssetPath(asset);

            if (path.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return;
            }

            Debug.LogWarning($"[PlatformConfigWriter] {path} is not inside a Resources folder, so " +
                             "Resources.Load will not find it and a player build will not contain it. " +
                             "It will still work in the editor, which is what makes this easy to miss. " +
                             $"Move it under a folder named Resources (the default location is {config_folder_path}).");
        }

        private static void EnsureFolderExists(string folderPath)
        {
            string[] folders = folderPath.Split('/');
            string currentPath = "";

            foreach (string folder in folders)
            {
                currentPath = Path.Combine(currentPath, folder);
                if (!AssetDatabase.IsValidFolder(currentPath))
                {
                    AssetDatabase.CreateFolder(Path.GetDirectoryName(currentPath), Path.GetFileName(currentPath));
                }
            }
        }
    }
}
