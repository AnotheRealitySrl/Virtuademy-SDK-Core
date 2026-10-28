using Virtuademy.SDK.Core.ApiSystem;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using UnityEditor;

using UnityEngine;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// The import of the file the Back Office downloads (<b>Download SDK Environments Config</b>), shared by the two
    /// windows that take it (meta-repo ADR 0037, "Windows per kind of project"). Where the credential goes depends on
    /// the kind of project:
    /// <list type="bullet">
    /// <item><description>An environment project (the Publish Environments window, and <c>EnvironmentsCli tenant-config</c>):
    /// into the encrypted connection store only, <see cref="ImportIntoStoreAsync"/>. No file stays in <c>Assets/</c>, and
    /// every environment project of the computer uses the connections.</description></item>
    /// <item><description>An application (the Available Tenants window): into the project, <see cref="ImportIntoProject"/>,
    /// because the tenant switch writes <c>PlatformCredentials.asset</c> from the selected file.</description></item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Nothing here logs or returns the file's content: it holds the app's secret.
    /// </remarks>
    public static class ConfigurationFileImport
    {
        /// <summary>What an import into the store did, without any secret.</summary>
        public sealed class StoreImportResult
        {
            /// <summary>The app name the file stands for: its name up to the first dot.</summary>
            public string App;

            /// <summary>The environment keys of the file that were imported.</summary>
            public List<string> Envs = new();

            /// <summary>The connections added or refreshed, one per imported environment.</summary>
            public List<ConnectionRecord> Connections = new();

            /// <summary>Per environment that could not become a connection: <c>&lt;env&gt;: &lt;reason&gt;</c>.</summary>
            public List<string> Failures = new();

            /// <summary>The environment keys among <see cref="Failures"/> whose app key is not a CreatorKit app (outcome <c>platform-mismatch</c>).</summary>
            public List<string> PlatformMismatches = new();

            public bool Success => Failures.Count == 0 && Connections.Count > 0;
        }

        /// <summary>Asks for the file, starting in the user's Downloads folder. Null when cancelled.</summary>
        public static string PickFile()
        {
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string file = EditorUtility.OpenFilePanel("Import the tenant configuration file", Directory.Exists(downloads) ? downloads : "", "json");
            return string.IsNullOrEmpty(file) ? null : file;
        }

        /// <summary>
        /// The file's credentials into the connection store, one connection per environment of the file (or only
        /// <paramref name="env"/>). Needs the network: a connection is named by the tenant key the platform
        /// gives (<see cref="ConnectionLoginService.AddConnectionAsync"/>). No login is made. A file whose app is not a CreatorKit
        /// app is refused (<c>platform-mismatch</c>), and no connection is added for it.
        /// </summary>
        /// <exception cref="InvalidOperationException">The file is missing, is not a tenant configuration, or has no
        /// such environment. The message never quotes the file.</exception>
        public static async Task<StoreImportResult> ImportIntoStoreAsync(string file, string env = null, Action<string> onStatus = null)
        {
            Dictionary<string, AppIdentification> parsed = AppConfigurationImporter.ReadFile(file);

            if (env != null && !parsed.ContainsKey(env))
                throw new InvalidOperationException($"The file has no environment '{env}'. It has: {string.Join(", ", parsed.Keys)}.");

            StoreImportResult result = new() { App = AppConfigurationImporter.AppNameOf(file) };

            foreach (KeyValuePair<string, AppIdentification> entry in parsed.Where(e => env == null || e.Key == env))
            {
                result.Envs.Add(entry.Key);

                LoginOutcome outcome = await ConnectionLoginService.AddConnectionAsync(entry.Value, result.App, entry.Key,
                    new LoginOptions { OnStatus = onStatus });

                if (outcome.Success)
                    result.Connections.Add(outcome.Connection);
                else
                {
                    result.Failures.Add($"{entry.Key}: {outcome.Message}");

                    if (outcome.Code == "platform-mismatch")
                        result.PlatformMismatches.Add(entry.Key);
                }
            }

            if (result.Connections.Count > 0)
                EditorLoginState.NotifyLoginChanged();

            Debug.Log($"[ConfigurationFileImport] {result.App}: {result.Connections.Count} connection(s) in the store, used by every environment project of this computer" +
                      (result.Connections.Count > 0 ? $" ({string.Join(", ", result.Connections.Select(c => $"{c.TenantLabel} {c.Environment}"))})" : "") +
                      (result.Failures.Count > 0 ? $"; not imported: {string.Join("; ", result.Failures)}" : "") + ".");

            return result;
        }

        /// <summary>
        /// The file into the project and onto <paramref name="settings"/>, selected (<see cref="AppConfigurationImporter.Import"/>),
        /// with the dialogs a person answers. When git does not ignore where the file would land, the import is refused
        /// and a dialog names the risk; only an explicit "Import anyway" repeats it with the override. That button is the
        /// dialog's third one, so Enter, Escape and a dialog nobody can answer (batch mode) all end in "not imported".
        /// </summary>
        /// <returns>What the import did, or null when it did not happen (refused, declined or failed; the Console says why).</returns>
        public static AppConfigurationImporter.ImportResult ImportIntoProject(string file, AppConfigurationSettings settings, bool allowTracked = false)
        {
            try
            {
                AppConfigurationImporter.ImportResult result = AppConfigurationImporter.Import(file, settings: settings, allowTracked: allowTracked);
                Debug.Log($"[ConfigurationFileImport] Imported {result.AssetPath}: {result.App} / {result.Env} is selected.");
                return result;
            }
            catch (AppConfigurationImporter.SecretNotIgnoredException ex)
            {
                Debug.LogError($"[ConfigurationFileImport] The configuration file was not imported: {ex.Message}");

                int choice = EditorUtility.DisplayDialogComplex("The configuration file would be committed",
                    $"The file holds the app's secret, and git does not ignore '{ex.AssetPath}'. " +
                    "Importing it there puts the secret one commit away from the repository (and from everyone who clones it).\n\n" +
                    $"Safer: add {AppConfigurationSettings.DefaultFolder}/ to the project's .gitignore, then import again.",
                    "Don't import", "Cancel", "Import anyway");

                return choice == 2 ? ImportIntoProject(file, settings, allowTracked: true) : null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConfigurationFileImport] The configuration file was not imported: {ex.Message}");
                EditorUtility.DisplayDialog("Import failed", ex.Message, "OK");
                return null;
            }
        }
    }
}
