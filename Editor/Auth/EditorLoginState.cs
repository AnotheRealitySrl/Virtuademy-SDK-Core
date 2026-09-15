using Newtonsoft.Json;

using Virtuademy.SDK.TenantConfiguration;

using System;
using System.Globalization;

using UnityEditor;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// Stores editor login state using both SessionState (for runtime) and EditorPrefs (for persistence across editor restarts).
    /// On domain reload, restores from EditorPrefs if SessionState is empty.
    ///
    /// Alongside the token this keeps everything needed to renew it without asking the user
    /// anything: the expiry, the client id and the resolved auth config. See
    /// <see cref="EditorSessionManager"/> for the renewal itself — nothing outside it should
    /// write the token.
    /// </summary>
    public static class EditorLoginState
    {
        private const string TOKEN_KEY = "Reflectis_EditorLogin_Token";
        private const string TOKEN_EXPIRY_KEY = "Reflectis_EditorLogin_TokenExpiry";
        private const string TOKEN_REJECTED_KEY = "Reflectis_EditorLogin_TokenRejected";
        private const string TENANT_KEY = "Reflectis_EditorLogin_Tenant";
        private const string USERNAME_KEY = "Reflectis_EditorLogin_Username";
        private const string IS_TENANT_MANAGER_KEY = "Reflectis_EditorLogin_IsTenantManager";
        private const string LOGGED_IN_APP_KEY = "Reflectis_EditorLogin_App";
        private const string LOGGED_IN_ENV_KEY = "Reflectis_EditorLogin_Env";
        private const string AUTH_CLIENT_ID_KEY = "Reflectis_EditorLogin_AuthClientId";
        private const string AUTH_CONFIG_KEY = "Reflectis_EditorLogin_AuthConfig";

        /// <summary>
        /// How long before the real expiry a token is already treated as stale. A single deploy
        /// step (zip upload, DLL verification) can take a while between the check and the request
        /// reaching the server, so the token must still be good on arrival, not just on departure.
        /// </summary>
        public static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(2);

        // Helper methods to read/write with EditorPrefs as persistent backing store
        private static string GetString(string key, string defaultValue = "")
        {
            string value = SessionState.GetString(key, "");
            if (string.IsNullOrEmpty(value))
            {
                value = EditorPrefs.GetString(key, defaultValue);
                if (!string.IsNullOrEmpty(value))
                    SessionState.SetString(key, value);
            }
            return value;
        }

        private static void SetString(string key, string value)
        {
            SessionState.SetString(key, value);
            EditorPrefs.SetString(key, value);
        }

        private static bool GetBool(string key, bool defaultValue = false)
        {
            // SessionState doesn't distinguish "not set" from "false", so check EditorPrefs first on cold start
            string marker = SessionState.GetString(key + "_set", "");
            if (string.IsNullOrEmpty(marker))
            {
                bool value = EditorPrefs.GetBool(key, defaultValue);
                SessionState.SetBool(key, value);
                SessionState.SetString(key + "_set", "1");
                return value;
            }
            return SessionState.GetBool(key, defaultValue);
        }

        private static void SetBool(string key, bool value)
        {
            SessionState.SetBool(key, value);
            SessionState.SetString(key + "_set", "1");
            EditorPrefs.SetBool(key, value);
        }

        public static string BearerToken
        {
            get => GetString(TOKEN_KEY);
            private set => SetString(TOKEN_KEY, value);
        }

        public static Tenant CurrentTenant
        {
            get
            {
                string json = GetString(TENANT_KEY);
                if (string.IsNullOrEmpty(json)) return null;
                try
                {
                    return JsonConvert.DeserializeObject<Tenant>(json);
                }
                catch
                {
                    return null;
                }
            }
            private set
            {
                SetString(TENANT_KEY, value != null ? JsonConvert.SerializeObject(value) : "");
            }
        }

        public static string Username
        {
            get => GetString(USERNAME_KEY);
            private set => SetString(USERNAME_KEY, value ?? "");
        }

        public static bool IsTenantManager
        {
            get => GetBool(IS_TENANT_MANAGER_KEY);
            private set => SetBool(IS_TENANT_MANAGER_KEY, value);
        }

        public static string LoggedInApp
        {
            get => GetString(LOGGED_IN_APP_KEY);
            private set => SetString(LOGGED_IN_APP_KEY, value ?? "");
        }

        public static string LoggedInEnv
        {
            get => GetString(LOGGED_IN_ENV_KEY);
            private set => SetString(LOGGED_IN_ENV_KEY, value ?? "");
        }

        /// <summary>
        /// Azure client id (the app registration) the session was obtained with. Needed to
        /// rebuild the MSAL client when renewing the token after a domain reload.
        /// </summary>
        public static string AuthClientId
        {
            get => GetString(AUTH_CLIENT_ID_KEY);
            private set => SetString(AUTH_CLIENT_ID_KEY, value ?? "");
        }

        /// <summary>
        /// The auth config as resolved at login time. Stored rather than re-read from the tenant
        /// because login falls back to the legacy app custom config when the tenant has no
        /// authConfig yet: a renewal must use whatever actually worked.
        /// </summary>
        public static AzureB2CConfig AuthConfig
        {
            get
            {
                string json = GetString(AUTH_CONFIG_KEY);
                if (string.IsNullOrEmpty(json)) return null;
                try
                {
                    return JsonConvert.DeserializeObject<AzureB2CConfig>(json);
                }
                catch
                {
                    return null;
                }
            }
            private set
            {
                SetString(AUTH_CONFIG_KEY, value != null ? JsonConvert.SerializeObject(value) : "");
            }
        }

        /// <summary>
        /// When the current token stops being accepted, or null if it could not be determined.
        /// </summary>
        public static DateTime? TokenExpiryUtc
        {
            get
            {
                // The claim inside the token wins over the stored value: see JwtBearerInspector.
                if (JwtBearerInspector.TryGetExpiry(BearerToken, out DateTime fromClaim))
                    return fromClaim;

                string stored = GetString(TOKEN_EXPIRY_KEY);
                if (!string.IsNullOrEmpty(stored)
                    && DateTime.TryParse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
                {
                    return parsed.ToUniversalTime();
                }

                return null;
            }
        }

        /// <summary>
        /// True when a login happened at some point, regardless of whether the token is still
        /// good. A session that only needs renewing is still a session.
        /// </summary>
        public static bool HasSession => !string.IsNullOrEmpty(BearerToken);

        /// <summary>
        /// True when the stored token can still be used: present, not rejected by the server,
        /// and not within <see cref="ExpiryMargin"/> of its expiry.
        /// </summary>
        public static bool IsTokenValid
        {
            get
            {
                if (!HasSession)
                    return false;

                if (GetString(TOKEN_REJECTED_KEY) == "1")
                    return false;

                DateTime? expiry = TokenExpiryUtc;
                if (expiry == null)
                {
                    // Unknown expiry — assume usable and let the 401 retry catch it.
                    return true;
                }

                return DateTime.UtcNow + ExpiryMargin < expiry.Value;
            }
        }

        /// <summary>
        /// True when the token can be renewed without a fresh interactive login being the only
        /// option: everything the renewal needs was stored at login time.
        /// </summary>
        public static bool CanRenewSession =>
            HasSession
            && CurrentTenant?.Config != null
            && AuthConfig != null
            && !string.IsNullOrEmpty(AuthClientId);

        /// <summary>
        /// A login is on record. This stays true while the token is merely expired, because every
        /// authenticated call renews it transparently — check <see cref="IsTokenValid"/> to know
        /// whether the next call will need a round trip to Azure first.
        /// </summary>
        public static bool IsLoggedIn => HasSession;

        /// <summary>
        /// Checks whether the given app/env pair matches the currently logged-in tenant.
        /// </summary>
        public static bool IsLoggedInto(string app, string env)
        {
            return IsLoggedIn
                && !string.IsNullOrEmpty(LoggedInApp)
                && LoggedInApp == app
                && LoggedInEnv == env;
        }

        public static event Action OnLoginStateChanged;

        public static void Set(string token, DateTime? tokenExpiryUtc, Tenant tenant, string username,
                               bool isTenantManager = false, string app = null, string env = null,
                               string authClientId = null, AzureB2CConfig authConfig = null)
        {
            BearerToken = token;
            SetExpiry(tokenExpiryUtc);
            CurrentTenant = tenant;
            Username = username;
            IsTenantManager = isTenantManager;
            LoggedInApp = app;
            LoggedInEnv = env;
            AuthClientId = authClientId;
            AuthConfig = authConfig;
            OnLoginStateChanged?.Invoke();
        }

        /// <summary>
        /// Replaces just the token after a renewal, leaving tenant, role and app/env untouched.
        /// A null or empty <paramref name="username"/> keeps the stored one.
        ///
        /// Deliberately does NOT raise <see cref="OnLoginStateChanged"/>: subscribers rebuild
        /// their UI and reload data on that event, and a token rotation happens *inside* their own
        /// API calls. Firing it here would re-enter them mid-request. Who the user is logged in as
        /// has not changed — only the token behind it.
        /// </summary>
        public static void UpdateToken(string token, DateTime? tokenExpiryUtc, string username = null)
        {
            BearerToken = token;
            SetExpiry(tokenExpiryUtc);

            if (!string.IsNullOrEmpty(username))
                Username = username;
        }

        /// <summary>
        /// Marks the token unusable without dropping the session, so the next authenticated call
        /// renews it. Called when the server answers 401 to a token we believed still valid
        /// (revoked, rotated, or clock skew between us and the API).
        /// Silent for the same reason as <see cref="UpdateToken"/>.
        /// </summary>
        public static void MarkTokenExpired()
        {
            SetString(TOKEN_REJECTED_KEY, "1");
        }

        public static void Clear()
        {
            BearerToken = "";
            SetString(TOKEN_EXPIRY_KEY, "");
            SetString(TOKEN_REJECTED_KEY, "");
            CurrentTenant = null;
            Username = "";
            IsTenantManager = false;
            LoggedInApp = "";
            LoggedInEnv = "";
            AuthClientId = "";
            AuthConfig = null;
            OnLoginStateChanged?.Invoke();
        }

        private static void SetExpiry(DateTime? expiryUtc)
        {
            SetString(TOKEN_EXPIRY_KEY, expiryUtc.HasValue
                ? expiryUtc.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
                : "");

            // A fresh token starts out accepted again.
            SetString(TOKEN_REJECTED_KEY, "");
        }
    }
}
