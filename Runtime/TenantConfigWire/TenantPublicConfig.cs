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
    }
}
