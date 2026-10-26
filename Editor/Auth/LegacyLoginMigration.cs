using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using UnityEditor;

using UnityEngine;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// What the move to per-user connections (ADR 0037) does about the login the editor used to keep per
    /// machine, in ten <c>Virtuademy_EditorLogin_*</c> keys of SessionState and EditorPrefs (the registry, on
    /// Windows).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The old login is not carried over: its token may belong to another tenant than the project's, and the
    /// connections are new records that ask for one login each. The credentials come over from the project's
    /// own configuration files (<see cref="ConnectionLoginService.ImportProjectCredentialsAsync"/>), not from
    /// these keys.
    /// </para>
    /// <para>
    /// Only the platform token, <c>Virtuademy_EditorLogin_Token</c>, is deleted: it sat in clear text in the
    /// registry. The other nine keys are left where they are and nothing reads them, so an older SDK on the
    /// same machine still finds its settings and only needs a new login. The token is deleted once per OS
    /// user (a marker in the store's folder says so), so an older SDK that logs in again afterwards is not
    /// logged out by every editor that starts. Where the store cannot be written (no encryption on this OS yet) it
    /// does not run at all: a login that would not survive the editor must not cost the user the one they have.
    /// </para>
    /// <para>
    /// The project's own MSAL caches, <c>Library/Virtuademy/Auth/msal_*.bin</c>, are deleted the first time the store
    /// is used in a project: they held a refresh token in clear text and nothing reads them any more
    /// (<see cref="DeleteProjectMsalCaches"/>).
    /// </para>
    /// </remarks>
    public static class LegacyLoginMigration
    {
        private const string legacy_token_key = "Virtuademy_EditorLogin_Token";
        private const string marker_file = "migration.json";

        // What reads and deletes the old keys: the editor's own storage, unless a test swaps it.
        internal static Func<string, bool> HasLegacyKey = key => EditorPrefs.HasKey(key) || SessionState.GetString(key, "") != "";
        internal static Action<string> DeleteLegacyKey = key =>
        {
            EditorPrefs.DeleteKey(key);
            SessionState.EraseString(key);
        };

        /// <summary>Runs the migration. Called by <see cref="ConnectionStore"/> on its first use in a process; never in a worker.</summary>
        internal static void Run()
        {
            if (ConnectionStore.IsWorkerProcess || !ConnectionStore.IsPersistent)
                return;

            try
            {
                string marker = Path.Combine(ConnectionStore.Root, marker_file);
                if (File.Exists(marker))
                    return;

                if (HasLegacyKey(legacy_token_key))
                {
                    DeleteLegacyKey(legacy_token_key);
                    Debug.Log("[LegacyLoginMigration] The editor's old login (the platform token in EditorPrefs) was deleted. " +
                              "Logins are per connection now: log in to each tenant once more.");
                }

                Directory.CreateDirectory(ConnectionStore.Root);
                ConnectionStore.WriteAtomic(marker, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(
                    new Dictionary<string, object> { { "legacyTokenHandledUtc", DateTime.UtcNow.ToString("o") } })));
            }
            catch (Exception ex)
            {
                // The migration is a cleanup: failing it must not stop a login.
                Debug.LogWarning($"[LegacyLoginMigration] Could not run: {ex.Message}");
            }
        }

        /// <summary>
        /// Deletes the old per-project MSAL caches (<c>Library/Virtuademy/Auth/msal_*.bin</c>): a refresh token in clear
        /// text, next to the project, that nothing reads since the accounts moved to the store's cache.
        /// </summary>
        internal static void DeleteProjectMsalCaches()
        {
            if (ConnectionStore.IsWorkerProcess)
                return;

            try
            {
                string folder = Path.Combine(ProjectConnections.ProjectFolder, "Library", "Virtuademy", "Auth");
                if (!Directory.Exists(folder))
                    return;

                foreach (string file in Directory.GetFiles(folder, "msal_*.bin"))
                {
                    File.Delete(file);
                    Debug.Log("[LegacyLoginMigration] Deleted the project's old token cache " + Path.GetFileName(file) + " (it held a refresh token in clear text).");
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is UnityException)
            {
                Debug.LogWarning($"[LegacyLoginMigration] Could not delete the project's old token cache: {ex.Message}");
            }
        }
    }
}
