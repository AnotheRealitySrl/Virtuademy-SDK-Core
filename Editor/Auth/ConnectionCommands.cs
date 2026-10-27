using Virtuademy.SDK.Core.ApiSystem;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using UnityEngine;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>What a connection command is asked to do. The command line and the request file both build one.</summary>
    public sealed class ConnectionCommandRequest
    {
        /// <summary><c>login</c>, <c>logout</c>, <c>connections</c>, <c>whoami</c>, or <c>status</c> (both of the last two).</summary>
        public string Command;

        /// <summary>A connection in the store: its key, tenant key, tenant label or <c>app/env</c>. Empty means the project's default one.</summary>
        public string Connection;

        /// <summary>For <c>login</c> of a connection that is not in the store yet: the app and the environment of the project's configuration file.</summary>
        public string App;
        public string Env;

        /// <summary>Print the authorization URL and wait, without opening the system browser.</summary>
        public bool NoBrowser;

        /// <summary>How long the user has, counted from the moment the URL is issued. Default three minutes.</summary>
        public int? LoginTimeoutSeconds;
    }

    /// <summary>What a connection command did. <see cref="Fields"/> never holds a token or a secret.</summary>
    public sealed class ConnectionCommandResult
    {
        /// <summary><c>done</c>, <c>login-required</c>, <c>connection-unknown</c>, <c>login-timeout</c>, <c>login-failed</c>, <c>store-not-persistent</c> or <c>bad-request</c>.</summary>
        public string Status;

        public string Error;

        public Dictionary<string, object> Fields = new();

        public bool Done => Status == "done";
    }

    /// <summary>
    /// The four commands that log in and out and say who is logged in, without a window: the
    /// <c>EnvironmentsCli</c> commands <c>login</c>, <c>logout</c>, <c>connections</c> and <c>whoami</c> and the
    /// request files an open editor picks up (<see cref="EditorRequestPump"/>) both run them here. A
    /// request may ask for no more than these: never a deploy.
    /// </summary>
    public static class ConnectionCommands
    {
        /// <summary>The commands an open editor accepts as a request file.</summary>
        public static readonly string[] RequestCommands = { "login", "logout", "status" };

        /// <summary>
        /// Runs a command. <paramref name="onProgress"/> is called while a login waits for the browser, with a
        /// <c>state</c> (<c>awaiting-browser</c>) and the <c>authUrl</c>, so a caller can show the URL before the
        /// command ends.
        /// </summary>
        public static async Task<ConnectionCommandResult> ExecuteAsync(ConnectionCommandRequest request,
                                                                       Action<Dictionary<string, object>> onProgress = null)
        {
            ConnectionCommandResult result = new();

            try
            {
                switch (request.Command)
                {
                    case "connections":
                        AddConnections(result);
                        break;

                    case "whoami":
                        WhoAmI(request, result);
                        break;

                    case "status":
                        // Both answers in one: what the store holds, and the connection asked for (or the default one).
                        AddConnections(result);
                        ConnectionRecord target = ResolveTarget(request, out string targetError);
                        if (target != null)
                            result.Fields["connection"] = target.ToSummary();
                        else if (!string.IsNullOrWhiteSpace(request.Connection))
                            (result.Status, result.Error) = ("connection-unknown", targetError);
                        break;

                    case "logout":
                        await LogoutAsync(request, result);
                        break;

                    case "login":
                        await LoginAsync(request, result, onProgress);
                        break;

                    default:
                        result.Status = "bad-request";
                        result.Error = $"Unknown command '{request.Command}'.";
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConnectionCommands] {request.Command} failed: {ex}");
                result.Status = "login-failed";
                result.Error = ex.Message;
            }

            result.Status ??= "done";
            return result;
        }

        #region Commands

        private static void AddConnections(ConnectionCommandResult result)
        {
            string defaultConnection = ProjectConnections.DefaultKey;
            IReadOnlyList<string> used = ProjectConnections.Used;

            result.Fields["connections"] = ConnectionStore.All().Select(c =>
            {
                Dictionary<string, object> summary = c.ToSummary();
                summary["usedByProject"] = used.Contains(c.Key, StringComparer.OrdinalIgnoreCase);
                summary["default"] = string.Equals(c.Key, defaultConnection, StringComparison.OrdinalIgnoreCase);
                return summary;
            }).ToList();

            result.Fields["store"] = new Dictionary<string, object>
            {
                { "persistent", ConnectionStore.IsPersistent },
                { "statusFile", ConnectionStore.IsPersistent ? ConnectionStore.StatusPath : null },
            };
        }

        private static void WhoAmI(ConnectionCommandRequest request, ConnectionCommandResult result)
        {
            ConnectionRecord record = ResolveTarget(request, out string error);
            if (record == null)
            {
                result.Status = "connection-unknown";
                result.Error = error;
                return;
            }

            result.Fields["connection"] = record.ToSummary();

            if (!record.HasSession)
            {
                result.Status = "login-required";
                result.Error = "Not logged in to this connection.";
            }
        }

        private static async Task LogoutAsync(ConnectionCommandRequest request, ConnectionCommandResult result)
        {
            ConnectionRecord record = ResolveTarget(request, out string error);
            if (record == null)
            {
                result.Status = "connection-unknown";
                result.Error = error;
                return;
            }

            await ConnectionLoginService.LogoutAsync(record.Key);
            result.Fields["connection"] = ConnectionStore.Get(record.Key)?.ToSummary();
        }

        private static async Task LoginAsync(ConnectionCommandRequest request, ConnectionCommandResult result,
                                             Action<Dictionary<string, object>> onProgress)
        {
            // A batch run ends with its process: where the store cannot be written, the login would be gone before anyone used it.
            if (Application.isBatchMode && !ConnectionStore.IsPersistent)
            {
                result.Status = "store-not-persistent";
                result.Error = "This OS has no encrypted connection store yet (macOS Keychain support is pending), so a login made in a batch run " +
                               "would end with the process. Log in from the open editor (Virtuademy > Available Tenants, or a login request file).";
                return;
            }

            LoginOptions options = new() { Interactive = true };
            options.Browser.OpenBrowser = !request.NoBrowser;

            if (request.LoginTimeoutSeconds is int seconds && seconds > 0)
                options.Browser.UserBudget = TimeSpan.FromSeconds(seconds);

            options.Browser.OnAuthUrl = url =>
            {
                result.Fields["authUrl"] = url;
                onProgress?.Invoke(new Dictionary<string, object>
                {
                    { "state", "awaiting-browser" },
                    { "authUrl", url },
                    { "expiresInSeconds", (int)options.Browser.UserBudget.TotalSeconds },
                    { "browserOpened", options.Browser.OpenBrowser },
                });
            };

            LoginOutcome outcome;

            if (string.IsNullOrWhiteSpace(request.Connection) && !string.IsNullOrWhiteSpace(request.App))
            {
                // A credential of the project's own configuration file, not in the store yet (or not named by key).
                if (!ConnectionLoginService.TryFindProjectCredential(request.App, request.Env, out AppIdentification identification,
                                                                     out string app, out string env, out string error))
                {
                    result.Status = "connection-unknown";
                    result.Error = error;
                    return;
                }

                outcome = await ConnectionLoginService.LoginAsync(identification, app, env, options);
            }
            else
            {
                ConnectionRecord record = ResolveTarget(request, out string error);
                if (record == null)
                {
                    // Nothing named and no default connection: the project's only configuration file is the way in.
                    if (string.IsNullOrWhiteSpace(request.Connection)
                        && ConnectionLoginService.TryFindProjectCredential(null, request.Env, out AppIdentification identification,
                                                                           out string app, out string env, out _))
                    {
                        outcome = await ConnectionLoginService.LoginAsync(identification, app, env, options);
                        Finish(result, outcome);
                        return;
                    }

                    result.Status = "connection-unknown";
                    result.Error = error;
                    return;
                }

                outcome = await ConnectionLoginService.LoginAsync(record.Key, options);
            }

            Finish(result, outcome);
        }

        private static void Finish(ConnectionCommandResult result, LoginOutcome outcome)
        {
            result.Fields["connection"] = (outcome.Connection != null ? ConnectionStore.Get(outcome.Connection.Key) ?? outcome.Connection : null)?.ToSummary();

            if (outcome.Success)
                return;

            result.Error = outcome.Message;
            result.Status = outcome.Code switch
            {
                "login-timeout" => "login-timeout",
                "login-required" => "login-required",
                _ => "login-failed",
            };
        }

        /// <summary>The connection a request names, or the project's default one when it names none.</summary>
        private static ConnectionRecord ResolveTarget(ConnectionCommandRequest request, out string error)
        {
            error = null;

            if (!string.IsNullOrWhiteSpace(request.Connection))
                return ConnectionStore.Find(request.Connection, out error);

            ConnectionRecord defaultConnection = ConnectionStore.Get(ProjectConnections.DefaultKey);
            if (defaultConnection == null)
                error = "The project has no default connection: name one with the connection argument (see the connections command).";

            return defaultConnection;
        }

        #endregion
    }
}
