using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using UnityEditor;

using UnityEngine;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// The entry point the SDK's setup package (<c>Virtuademy-SDK-Environments-Setup</c>) calls to work with the computer's
    /// connections. It calls this class <b>by name, through reflection</b>, only after SDK-Core is installed: the setup package
    /// installs the SDK and cannot reference it. For that reason everything goes in and out as strings, so the contract never
    /// depends on an SDK type.
    /// </summary>
    /// <remarks>
    /// The members are a contract, versioned by <see cref="Version"/>: <b>do not rename or remove one, and do not change a
    /// JSON shape</b>; raise <see cref="Version"/> on any change. Each method wraps a service that exists for other callers
    /// (<see cref="ConfigurationFileImport"/>, <see cref="ConnectionLoginService"/>, <see cref="ProjectConnections"/>) and
    /// adds nothing of its own. Call them on the editor's main thread.
    /// <para>
    /// A <see cref="Task{TResult}"/> result is one JSON object:
    /// <c>{ "ok": bool, "code": string, "message": string|null, "connection": summary|null }</c>. <c>code</c> is <c>ok</c> or
    /// the <see cref="LoginOutcome.Code"/> of the service, or one of <c>not-found</c> (no such connection), <c>worker</c> (an
    /// asset-import worker process, which never touches the store), <c>busy</c> (an operation on that connection is already
    /// running), <c>store-not-persistent</c> (a batch login where nothing can be encrypted), <c>invalid-file</c>,
    /// <c>import-failed</c> or <c>error</c>. A summary is <see cref="ConnectionRecord.ToSummary"/>. No result holds a token,
    /// a secret or a credential, and none throws: an exception comes back as <c>error</c>.
    /// </para>
    /// </remarks>
    public static class SetupBridge
    {
        /// <summary>Raised on any change of the members of this class or of the JSON they return.</summary>
        public const int Version = 1;

        // One operation per connection at a time: the setup page can be clicked again while a browser login waits.
        private static readonly HashSet<string> keysInUse = new(StringComparer.OrdinalIgnoreCase);
        private static bool importing;

        /// <summary>The computer's connections (<see cref="ProjectConnections.All"/>) as a JSON array of summaries; <c>[]</c> when there is none.</summary>
        public static string Connections()
        {
            try
            {
                if (AssetDatabase.IsAssetImportWorkerProcess())
                    return "[]";

                return JsonConvert.SerializeObject(ProjectConnections.All().Select(c => c.ToSummary()).ToList());
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SetupBridge] The connections could not be listed: {ex.Message}");
                return "[]";
            }
        }

        /// <summary>
        /// Imports the Back Office configuration file into the connection store (<see cref="ConfigurationFileImport.ImportIntoStoreAsync"/>),
        /// one connection per environment of the file; no login. Besides the common fields the result holds <c>app</c>,
        /// <c>connections</c> (the summaries added or refreshed), <c>failures</c> and <c>platformMismatches</c> (environment keys).
        /// </summary>
        public static async Task<string> ImportConfigurationFileAsync(string path)
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return Result(false, "worker", "This process does not use the connection store.");

            if (importing)
                return Result(false, "busy", "A configuration file is already being imported.");

            importing = true;

            try
            {
                ConfigurationFileImport.StoreImportResult imported;

                try
                {
                    imported = await ConfigurationFileImport.ImportIntoStoreAsync(path);
                }
                catch (InvalidOperationException ex)
                {
                    // The message never quotes the file (see ImportIntoStoreAsync).
                    return Result(false, "invalid-file", ex.Message);
                }

                bool onlyMismatches = imported.Failures.Count > 0 && imported.PlatformMismatches.Count == imported.Failures.Count;

                return Result(imported.Success,
                              imported.Success ? "ok" : onlyMismatches ? "platform-mismatch" : "import-failed",
                              imported.Failures.Count == 0 ? null : string.Join("; ", imported.Failures),
                              extra: new Dictionary<string, object>
                              {
                                  { "app", imported.App },
                                  { "connections", imported.Connections.Select(c => c.ToSummary()).ToList() },
                                  { "failures", imported.Failures },
                                  { "platformMismatches", imported.PlatformMismatches },
                              });
            }
            catch (Exception ex)
            {
                return Failure(ex);
            }
            finally
            {
                importing = false;
            }
        }

        /// <summary>
        /// Logs a connection in, as the <c>login</c> command does: the system browser opens for the user, who has three minutes
        /// from the moment the page is issued. Silent while the connection's account still has a refresh token.
        /// </summary>
        public static Task<string> LoginAsync(string connectionKey) =>
            RunOnConnectionAsync(connectionKey, async record =>
            {
                // A batch run ends with its process: where the store cannot be written the login would be gone before anyone used it.
                if (Application.isBatchMode && !ConnectionStore.IsPersistent)
                    return Result(false, "store-not-persistent", "This OS has no encrypted connection store yet, so a login made in a batch run would end with the process.");

                LoginOutcome outcome = await ConnectionLoginService.LoginAsync(record.Key, new LoginOptions { Interactive = true });
                return OutcomeResult(outcome, record.Key);
            });

        /// <summary>Ends the connection's session (<see cref="ConnectionLoginService.LogoutAsync"/>); the connection stays in the store.</summary>
        public static Task<string> LogoutAsync(string connectionKey) =>
            RunOnConnectionAsync(connectionKey, async record =>
            {
                if (!await ConnectionLoginService.LogoutAsync(record.Key))
                    return Result(false, "not-found", $"There is no connection '{record.Key}'.");

                return Result(true, "ok", null, ConnectionStore.Get(record.Key));
            });

        /// <summary>
        /// Removes the connection from this computer (<see cref="ConnectionLoginService.RemoveFromComputerAsync"/>): it is logged out first,
        /// then its record leaves the store, for every project.
        /// </summary>
        public static Task<string> RemoveFromComputerAsync(string connectionKey) =>
            RunOnConnectionAsync(connectionKey, async record =>
            {
                if (!await ConnectionLoginService.RemoveFromComputerAsync(record.Key))
                    return Result(false, "remove-failed", "The connection could not be removed: the store is locked, or the connection is gone.");

                return Result(true, "ok", null);
            });

        /// <summary>
        /// Reads the connection's tenant again over HMAC, with no login (<see cref="ConnectionLoginService.RefreshConnectionAsync"/>): its
        /// platform version and the platform of its app key, which an operator can change at any time.
        /// </summary>
        public static Task<string> RefreshAsync(string connectionKey) =>
            RunOnConnectionAsync(connectionKey, async record =>
                OutcomeResult(await ConnectionLoginService.RefreshConnectionAsync(record.Key), record.Key));

        #region Helpers

        /// <summary>Checks the process and the key, takes the connection's turn and runs the operation; every exit is a JSON string.</summary>
        private static async Task<string> RunOnConnectionAsync(string connectionKey, Func<ConnectionRecord, Task<string>> operation)
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return Result(false, "worker", "This process does not use the connection store.");

            string taken = null;

            try
            {
                ConnectionRecord record = ConnectionStore.Get(connectionKey);
                if (record == null)
                    return Result(false, "not-found", $"There is no connection '{connectionKey}'.");

                if (!keysInUse.Add(record.Key))
                    return Result(false, "busy", "Another operation on this connection is still running.");

                taken = record.Key;
                return await operation(record);
            }
            catch (Exception ex)
            {
                return Failure(ex);
            }
            finally
            {
                if (taken != null)
                    keysInUse.Remove(taken);
            }
        }

        private static string OutcomeResult(LoginOutcome outcome, string key) =>
            Result(outcome.Success, outcome.Code ?? (outcome.Success ? "ok" : "error"), outcome.Success ? null : outcome.Message,
                   ConnectionStore.Get(key) ?? outcome.Connection);

        private static string Failure(Exception ex)
        {
            Debug.LogError($"[SetupBridge] {ex}");
            return Result(false, "error", ex.Message);
        }

        private static string Result(bool ok, string code, string message, ConnectionRecord connection = null,
                                     Dictionary<string, object> extra = null)
        {
            Dictionary<string, object> result = new()
            {
                { "ok", ok },
                { "code", code },
                { "message", message },
                { "connection", connection?.ToSummary() },
            };

            if (extra != null)
                foreach (KeyValuePair<string, object> field in extra)
                    result[field.Key] = field.Value;

            return JsonConvert.SerializeObject(result);
        }

        #endregion
    }
}
