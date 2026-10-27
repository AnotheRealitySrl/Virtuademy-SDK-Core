using Virtuademy.SDK.TenantConfiguration;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

using Microsoft.Identity.Client;

using UnityEditor;

using UnityEngine;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// Owns the editor's authenticated sessions: renews them before they expire, and replays a request once
    /// when the server rejects a token we believed was still good. A session belongs to a connection of the
    /// per-user <see cref="ConnectionStore"/>; every method has a form that names the connection, and the one
    /// that does not uses the project's default connection (<see cref="ProjectConnections.DefaultKey"/>).
    ///
    /// Every editor tool that calls a Virtuademy API must go through
    /// <see cref="SendAuthorizedAsync(Func{HttpRequestMessage}, HttpClient, bool)"/> (or at least await
    /// <see cref="EnsureValidTokenAsync(bool)"/> immediately before each request). Reading the token once and
    /// reusing it across a long operation is exactly the bug this class exists to prevent: a
    /// build-and-deploy runs for minutes, and the token can die halfway through it.
    /// </summary>
    public static class EditorSessionManager
    {
        private static readonly HttpClient httpClient = new();

        // One renewal per connection at a time in this process; across processes, the store's renewal lock.
        private static readonly object renewalLock = new();
        private static readonly Dictionary<string, Task<bool>> renewalsInFlight = new();

        // Read once, on whichever thread first touches this class (the main one in practice):
        // Application.isBatchMode is an engine call and the renewal below may run elsewhere.
        private static readonly bool runsInBatchMode = Application.isBatchMode;

        /// <summary>
        /// Whether a session may fall back to an interactive login (the system browser) when it
        /// cannot be renewed silently. False in batch mode: nobody is there to answer the browser,
        /// so the editor would sit waiting for a login that cannot happen. Every
        /// <c>allowInteractive</c> argument below is read through it, so no caller has to remember.
        /// The <c>login</c> command line is the exception that proves it: it is asked for the browser, by name
        /// (<see cref="ConnectionLoginService.LoginAsync(string, LoginOptions)"/>), and says where to find it.
        /// </summary>
        public static bool InteractiveLoginAllowed => !runsInBatchMode;

        /// <summary>
        /// Guarantees a usable token for the project's default connection, renewing it if needed.
        /// </summary>
        public static Task<bool> EnsureValidTokenAsync(bool allowInteractive = true) =>
            EnsureValidTokenAsync(ProjectConnections.DefaultKey, allowInteractive);

        /// <summary>
        /// Guarantees a usable token for the connection, renewing it if needed. Renewal is silent whenever the
        /// MSAL cache still holds a refresh token for the connection's account; otherwise it falls back to an
        /// interactive login when <paramref name="allowInteractive"/> allows it — and never in batch mode
        /// (<see cref="InteractiveLoginAllowed"/>).
        ///
        /// Concurrent callers share a single renewal — N parallel requests finding the same
        /// expired token must not trigger N logins — and so do processes (the store's renewal lock): one renews,
        /// the others read what it stored.
        /// </summary>
        /// <returns>False when there is no session at all, or the renewal failed. The caller must
        /// abort: no request made with the current token can succeed.</returns>
        public static Task<bool> EnsureValidTokenAsync(string connectionKey, bool allowInteractive = true)
        {
            allowInteractive &= InteractiveLoginAllowed;

            ConnectionRecord record = ConnectionStore.Get(connectionKey);
            if (record == null || !record.HasSession)
            {
                Debug.LogError("[EditorSessionManager] Not logged in. Log in via 'Virtuademy / Publish Environments'.");
                return Task.FromResult(false);
            }

            if (record.IsTokenValid)
                return Task.FromResult(true);

            lock (renewalLock)
            {
                if (renewalsInFlight.TryGetValue(record.Key, out Task<bool> running) && !running.IsCompleted)
                    return running;

                Task<bool> renewal = RenewSessionAsync(record.Key, allowInteractive);
                renewalsInFlight[record.Key] = renewal;
                return renewal;
            }
        }

        /// <summary>
        /// Sends an authenticated request on the project's default connection.
        /// </summary>
        public static Task<HttpResponseMessage> SendAuthorizedAsync(Func<HttpRequestMessage> requestFactory,
                                                                    HttpClient client = null,
                                                                    bool allowInteractive = true) =>
            SendAuthorizedAsync(ProjectConnections.DefaultKey, requestFactory, client, allowInteractive);

        /// <summary>
        /// Sends an authenticated request on a connection, attaching a freshly validated token.
        ///
        /// The request is built by <paramref name="requestFactory"/> rather than passed in
        /// because an <see cref="HttpRequestMessage"/> cannot be sent twice: the 401 retry needs
        /// to build a second one.
        /// </summary>
        /// <returns>The response, or null when the session could not be established — which is
        /// not the same as a failed request and callers should report it as such.</returns>
        public static async Task<HttpResponseMessage> SendAuthorizedAsync(string connectionKey,
                                                                         Func<HttpRequestMessage> requestFactory,
                                                                         HttpClient client = null,
                                                                         bool allowInteractive = true)
        {
            if (requestFactory == null)
                throw new ArgumentNullException(nameof(requestFactory));

            if (!await EnsureValidTokenAsync(connectionKey, allowInteractive))
                return null;

            HttpClient targetClient = client ?? httpClient;

            HttpResponseMessage response = await SendOnceAsync(connectionKey, requestFactory, targetClient);

            if (response.StatusCode != HttpStatusCode.Unauthorized)
                return response;

            // The server rejected a token our own expiry check considered valid: revoked,
            // rotated, or our clock disagrees with the API's. Renew once and replay.
            Debug.LogWarning("[EditorSessionManager] Request rejected with 401 — renewing the session and retrying once.");
            response.Dispose();
            await MarkTokenRejectedAsync(connectionKey);

            if (!await EnsureValidTokenAsync(connectionKey, allowInteractive))
                return null;

            return await SendOnceAsync(connectionKey, requestFactory, targetClient);
        }

        /// <summary>
        /// Ends the project's default connection's session on both sides: the stored token and the MSAL refresh
        /// token (which stays when another connection uses the same account).
        /// </summary>
        public static async Task LogoutAsync() => await LogoutAsync(ProjectConnections.DefaultKey);

        public static async Task LogoutAsync(string connectionKey)
        {
            if (!await ConnectionLoginService.LogoutAsync(connectionKey))
                Debug.Log("[EditorSessionManager] There was no session to end.");
        }

        /// <summary>
        /// Human-readable session state, for status labels.
        /// </summary>
        public static string DescribeExpiry()
        {
            DateTime? expiry = EditorLoginState.TokenExpiryUtc;
            if (expiry == null)
                return "unknown";

            return FormatLocal(expiry, seconds: true);
        }

        /// <summary>
        /// A UTC moment as the labels and logs show it, in local time: the time alone when it falls today, the day and
        /// month too otherwise, so a session that lasts a week does not read as one that ends today.
        /// </summary>
        internal static string FormatLocal(DateTime? utc, bool seconds = false)
        {
            if (utc == null)
                return "unknown";

            DateTime local = utc.Value.ToLocalTime();
            string time = local.ToString(seconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);
            return local.Date == DateTime.Now.Date ? time : local.ToString("d MMM ", CultureInfo.InvariantCulture) + time;
        }

        /// <summary>
        /// The session part of the login status labels. An expired token is not an error state —
        /// the next operation renews it, silently while Azure still holds a refresh token — so the
        /// text says when it expired and that nothing needs doing, instead of a green "logged in"
        /// that hides a possible login prompt. The time shown is the one at which the label
        /// changes (<see cref="EditorLoginState.TokenUsableUntilUtc"/>), not the raw expiry.
        /// </summary>
        public static string DescribeSession(out bool tokenValid) =>
            DescribeSession(ConnectionStore.Get(ProjectConnections.DefaultKey), out tokenValid);

        /// <summary>The same for one connection: the default one or any other.</summary>
        public static string DescribeSession(ConnectionRecord record, out bool tokenValid)
        {
            tokenValid = record?.IsTokenValid ?? false;
            DateTime? usableUntil = record?.UsableUntilUtc;
            string time = FormatLocal(usableUntil);

            if (tokenValid)
                return $" (session until {time})";

            if (record?.TokenRejected ?? false)
                return " - session rejected by the server, it will be renewed automatically on the next operation";

            return $" - session expired at {time}, it will be renewed automatically on the next operation";
        }

        private static async Task<HttpResponseMessage> SendOnceAsync(string connectionKey, Func<HttpRequestMessage> requestFactory, HttpClient client)
        {
            ConnectionRecord record = ConnectionStore.Get(connectionKey);
            using HttpRequestMessage request = requestFactory();

            // A token goes only to the hosts of the tenant of the connection it belongs to: a caller that took its
            // address from another connection is stopped here.
            RequireHostOf(record, request.RequestUri, connectionKey);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", record?.Token);
            return await client.SendAsync(request);
        }

        private static void RequireHostOf(ConnectionRecord record, Uri uri, string connectionKey)
        {
            TenantConfig config = record?.Tenant?.Config;
            string[] own = { config?.ApplicationApiUrl, config?.ProfileApiUrl, config?.RealtimeApiUrl, config?.AIApiUrl };

            bool belongs = uri != null && uri.IsAbsoluteUri && own.Any(url =>
                Uri.TryCreate(url, UriKind.Absolute, out Uri host) && string.Equals(host.Authority, uri.Authority, StringComparison.OrdinalIgnoreCase));

            if (!belongs)
            {
                throw new InvalidOperationException(
                    $"The request to {uri?.GetLeftPart(UriPartial.Authority) ?? "(no address)"} was not sent: it is not an address of the tenant of connection '{connectionKey}', " +
                    "and that connection's token goes nowhere else.");
            }
        }

        /// <summary>
        /// Marks the token unusable without dropping the session, so the next authenticated call
        /// renews it. Raises <see cref="EditorLoginState.OnTokenChanged"/> only.
        /// </summary>
        private static async Task MarkTokenRejectedAsync(string connectionKey)
        {
            await ConnectionStore.UpdateAsync(connectionKey, r =>
            {
                if (r != null)
                    r.TokenRejected = true;
                return r;
            });

            EditorLoginState.NotifyTokenChanged();
        }

        private static async Task<bool> RenewSessionAsync(string key, bool allowInteractive)
        {
            ConnectionRecord record = ConnectionStore.Get(key);
            if (record == null)
                return false;

            if (!record.CanRenew)
            {
                // The auth config and client id needed to talk to Azure were never written down.
                Debug.LogError("[EditorSessionManager] The stored session cannot be renewed automatically. " +
                               "Log in again via 'Virtuademy / Publish Environments'.");
                return false;
            }

            long seenRevision = record.Revision;
            IPublicClientApplication client = AzureAuthService.GetClient(record.AuthClientId, record.AuthConfig);
            string[] scopes = AzureAuthService.BuildScopes(record.AuthConfig);
            AzureTokenResult azure = null;

            // One process renews, the others read the result. The lock covers the silent call only: a person at
            // a browser takes minutes, and everybody else would wait for them.
            using (IDisposable renewal = await ConnectionStore.LockRenewalAsync(key, 60000))
            {
                ConnectionRecord now = ConnectionStore.Get(key);
                if (now != null && now.Revision != seenRevision && now.IsTokenValid)
                {
                    Debug.Log($"[EditorSessionManager] Another process renewed the session of {now.TenantLabel}; using it.");
                    EditorLoginState.NotifyTokenChanged();
                    return true;
                }

                record = now ?? record;
                azure = await AzureAuthService.TryAcquireTokenSilent(client, scopes, record.AccountId);

                if (azure != null)
                    return await StoreRenewalAsync(key, record, azure);
            }

            if (!allowInteractive)
            {
                Debug.LogError("[EditorSessionManager] The session expired and could not be renewed silently.");
                return false;
            }

            // An interactive login opens the system browser. A modal progress bar left up by
            // the operation that triggered this would sit on top of it, unclosable.
            EditorUtility.ClearProgressBar();
            Debug.Log("[EditorSessionManager] The session expired and cannot be renewed silently — asking for a new login.");

            try
            {
                azure = await AzureAuthService.LoginInteractive(client, scopes, record.AccountId, new InteractiveLoginOptions());
            }
            catch (Exception ex) when (ex is LoginTimeoutException || ex is LoginAccountMismatchException || ex is MsalException || ex is OperationCanceledException)
            {
                Debug.LogError($"[EditorSessionManager] Interactive re-login failed: {ex.Message}");
                return false;
            }

            if (string.IsNullOrEmpty(azure?.AccessToken))
            {
                Debug.LogError("[EditorSessionManager] Interactive re-login returned no access token.");
                return false;
            }

            return await StoreRenewalAsync(key, record, azure);
        }

        private static async Task<bool> StoreRenewalAsync(string key, ConnectionRecord record, AzureTokenResult azure)
        {
            ConnectionRecord stored = await ConnectionLoginService.ExchangeAndStoreAsync(key, azure, checkTenantManager: false);
            if (stored == null)
                return false;

            EditorLoginState.NotifyTokenChanged();
            Debug.Log($"[EditorSessionManager] Session renewed for tenant {record.TenantLabel}, token expires at " +
                      $"{FormatLocal(stored.ExpiryUtc, seconds: true)}.");
            return true;
        }
    }
}
