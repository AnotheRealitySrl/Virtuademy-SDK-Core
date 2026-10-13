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
    }
}
