using System;
using System.Collections.Generic;

using UnityEngine;

namespace Virtuademy.SDK.TenantConfiguration
{
    /// <summary>
    /// Whitelisted public projection of <c>tenant.ctn_config</c> returned by
    /// <c>GET /manage/apps/tenant/config-public</c> (HMAC, pre-login).
    /// <para>
    /// The endpoint exposes only the fields declared here — every other
    /// <c>ctn_config</c> key stays server-side. Adding a new field requires
    /// a coordinated PR across Configuration-API + every consumer (this
    /// DTO + the Vue stores in Landing/Backoffice). See
    /// <c>docs/localization.md</c> in the meta-repo for the full contract,
    /// cascade and operator playbook.
    /// </para>
    /// </summary>
    [Serializable, Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.Fields)]
    public class TenantPublicConfig
    {
        [SerializeField] private List<string> supportedLanguages = new();
        [SerializeField] private string platformVersion;
        [SerializeField] private int? tenantKey;
        [SerializeField] private string environment;
        [SerializeField] private string backofficeUrl;
        [SerializeField] private TenantPublicAppConfig app;

        /// <summary>
        /// Languages this tenant supports, in order of preference. First
        /// element = default language for users without a stored
        /// preference. Empty / missing → consumers fail secure to a
        /// synthetic <c>["en"]</c> (single-language, switcher hidden).
        /// </summary>
        public List<string> SupportedLanguages => supportedLanguages;

        /// <summary>
        /// Platform version the tenant is pinned to, <c>year.minor.patch</c>
        /// (<c>2026.6.0</c>). Null when the tenant is not pinned to a version,
        /// or when the Configuration-API predates the field.
        /// </summary>
        public string PlatformVersion => string.IsNullOrWhiteSpace(platformVersion) ? null : platformVersion;

        /// <summary>
        /// Id of the tenant row (<c>config.tenants.ctn_id</c>). Not unique across Configuration-API
        /// instances: each one has its own Platform DB, so the editor's connection key is this id
        /// together with the instance's base URL. Null for an app with no tenant (a platform-admin
        /// app), which therefore cannot become a connection.
        /// </summary>
        public int? TenantKey => tenantKey;

        /// <summary>
        /// Environment of the tenant row, verbatim (<c>Sandbox</c>, <c>PreProduction</c>,
        /// <c>Production</c>). For display only: operators can edit it, so it never identifies anything.
        /// </summary>
        public string Environment => string.IsNullOrWhiteSpace(environment) ? null : environment;

        /// <summary>
        /// The tenant's Back Office address (<c>ctn_config.backofficeUrl</c>), an absolute http(s) URL the
        /// Configuration-API has already normalized. Null when the tenant has none, or when the
        /// Configuration-API predates the field (2026.7.0): the editor then shows no Back Office button.
        /// </summary>
        public string BackofficeUrl => string.IsNullOrWhiteSpace(backofficeUrl) ? null : backofficeUrl.Trim();

        /// <summary>
        /// The calling app: what it is (<see cref="TenantPublicAppConfig.Platform"/>) and the origins it accepts and offers.
        /// Null when the app has neither a platform nor a custom configuration, or when the Configuration-API predates the
        /// object. The call is signed by the app itself, so this says nothing about any other app.
        /// </summary>
        public TenantPublicAppConfig App => app;
    }

    /// <summary>
    /// The <c>app</c> object of <c>config-public</c>: the calling app's own block. Every field is optional.
    /// </summary>
    [Serializable, Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.Fields)]
    public class TenantPublicAppConfig
    {
        [SerializeField] private string platform;
        [SerializeField] private List<string> acceptedOrigins;
        [SerializeField] private List<string> offeredOrigins;

        /// <summary>
        /// The app's platform tag (<c>cap_platform</c>), as stored: which kind of client the credential was made for
        /// (<c>CreatorKit</c>, <c>WebGL</c>, <c>Mobile</c>, <c>VR</c>, <c>BO</c>...). A free string. Null when the column is
        /// blank, or when the Configuration-API predates the field (2026.7.0).
        /// </summary>
        public string Platform => string.IsNullOrWhiteSpace(platform) ? null : platform.Trim();

        /// <summary>The origins (<c>idp</c> values) the app accepts on return from the identity provider. Null when not set.</summary>
        public List<string> AcceptedOrigins => acceptedOrigins;

        /// <summary>The origins the app draws as sign-in buttons. Null (offer whatever is accepted) differs from empty (offer none).</summary>
        public List<string> OfferedOrigins => offeredOrigins;
    }
}
