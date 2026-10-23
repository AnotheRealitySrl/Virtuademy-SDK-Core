using Newtonsoft.Json;

using Virtuademy.SDK.Core.ApiSystem;
using Virtuademy.SDK.Core.Utilities;

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// One connection: one tenant of one Configuration-API instance, with the app credential imported
    /// from the Back Office file and the session of the user logged into it (ADR 0037).
    /// </summary>
    /// <remarks>
    /// The key is the normalized Configuration-API base URL plus the tenant key from
    /// <c>config-public</c> (<see cref="MakeKey"/>): the tenant key alone is not unique, because every
    /// Configuration-API instance has its own Platform DB. The environment and the label are for display.
    /// <para>
    /// A record read from <see cref="ConnectionStore"/> is a shared snapshot: read it, do not change it.
    /// <see cref="ConnectionStore.Update"/> hands out a copy to change.
    /// </para>
    /// </remarks>
    public sealed class ConnectionRecord
    {
        public string Key { get; set; }

        /// <summary>The Configuration-API instance, normalized (<see cref="NormalizeBaseUrl"/>).</summary>
        public string ConfigApiBaseUrl { get; set; }

        public int TenantKey { get; set; }

        /// <summary>For display only: the tenant's label.</summary>
        public string TenantLabel { get; set; }

        /// <summary>For display only: operators can edit it.</summary>
        public string Environment { get; set; }

        /// <summary>The platform version the tenant was pinned to when the connection was last resolved.</summary>
        public string PlatformVersion { get; set; }

        /// <summary>The app name of the imported file (its name up to the first dot).</summary>
        public string App { get; set; }

        /// <summary>The environment key inside the imported file.</summary>
        public string Env { get; set; }

        // The credential, as imported from the Back Office file.
        public string AppId { get; set; }
        public string AppSecret { get; set; }
        public string ApiVersion { get; set; }

        // The session.
        public string Token { get; set; }
        public DateTime? TokenExpiryUtc { get; set; }
        public bool TokenRejected { get; set; }
        public string Username { get; set; }
        public bool IsTenantManager { get; set; }

        /// <summary>The tenant as the platform answered at login: its API addresses among them.</summary>
        public Tenant Tenant { get; set; }

        /// <summary>The Azure client id (the app registration) the session was obtained with.</summary>
        public string AuthClientId { get; set; }

        /// <summary>The auth config as resolved at login: a renewal uses whatever actually worked.</summary>
        public AzureB2CConfig AuthConfig { get; set; }

        /// <summary>The MSAL account this connection is pinned to: its home account id.</summary>
        public string AccountId { get; set; }

        /// <summary>Raised by one on every change, so a reader knows another process renewed or logged out.</summary>
        public long Revision { get; set; }

        public DateTime UpdatedUtc { get; set; }

        #region Session

        [JsonIgnore] public bool HasSession => !string.IsNullOrEmpty(Token);

        [JsonIgnore]
        public bool CanRenew => HasSession && Tenant?.Config != null && AuthConfig != null && !string.IsNullOrEmpty(AuthClientId);

        /// <summary>The claim inside the token wins over the stored value: see <see cref="JwtBearerInspector"/>.</summary>
        [JsonIgnore]
        public DateTime? ExpiryUtc =>
            JwtBearerInspector.TryGetExpiry(Token, out DateTime fromClaim) ? fromClaim : TokenExpiryUtc?.ToUniversalTime();

        [JsonIgnore] public DateTime? UsableUntilUtc => ExpiryUtc - EditorLoginState.ExpiryMargin;

        [JsonIgnore]
        public bool IsTokenValid
        {
            get
            {
                if (!HasSession || TokenRejected)
                    return false;

                // Unknown expiry: assume usable and let the 401 retry catch it.
                DateTime? expiry = ExpiryUtc;
                return expiry == null || DateTime.UtcNow + EditorLoginState.ExpiryMargin < expiry.Value;
            }
        }

        /// <summary>The credential in the shape the Configuration-API client signs its calls with.</summary>
        public AppIdentification ToAppIdentification()
        {
            HmacCredential credential = new() { AppSecret = AppSecret };
            if (Guid.TryParse(AppId, out Guid id))
                credential.AppId = id;

            return new AppIdentification(credential, ConfigApiBaseUrl, ApiVersion);
        }

        #endregion

        #region Naming

        public static string MakeKey(string configApiBaseUrl, int tenantKey) =>
            NormalizeBaseUrl(configApiBaseUrl) + "#" + tenantKey.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Scheme and host in lower case, no default port, no query or fragment, no trailing slash. The path
        /// stays: an on-premises instance can sit under one.
        /// </summary>
        public static string NormalizeBaseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri uri))
                return url.Trim().TrimEnd('/').ToLowerInvariant();

            string path = uri.AbsolutePath.TrimEnd('/');
            return $"{uri.Scheme.ToLowerInvariant()}://{uri.Authority.ToLowerInvariant()}{path}";
        }

        #endregion

        /// <summary>
        /// What a command or a status label may show: no token, no secret, no account id. The same shape
        /// goes into the status file and into the result of <c>connections</c> and <c>whoami</c>.
        /// </summary>
        public Dictionary<string, object> ToSummary() => new()
        {
            { "key", Key },
            { "configApiBaseUrl", ConfigApiBaseUrl },
            { "tenantKey", TenantKey },
            { "tenantLabel", TenantLabel },
            { "environment", Environment },
            { "platformVersion", PlatformVersion },
            { "app", App },
            { "env", Env },
            { "loggedIn", HasSession },
            { "user", string.IsNullOrEmpty(Username) ? null : Username },
            { "tenantManager", IsTenantManager },
            { "tokenValid", IsTokenValid },
            { "tokenRejected", TokenRejected },
            { "tokenExpiresUtc", ExpiryUtc?.ToString("o", CultureInfo.InvariantCulture) },
            { "usableUntilUtc", UsableUntilUtc?.ToString("o", CultureInfo.InvariantCulture) },
            { "canRenew", CanRenew },
            { "revision", Revision },
        };
    }
}
