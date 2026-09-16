using System;
using System.Text.RegularExpressions;

namespace Virtuademy.SDK.Core.ApiSystem
{
    /// <summary>
    /// The URI scheme the platform uses to launch an application, computed from the application's
    /// own id rather than recorded anywhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The platform launches an external application by opening <c>scheme://…?authSessionHash=…</c>
    /// through an <c>ACTION_VIEW</c> intent. For that to reach anything, the application's manifest
    /// has to claim the same scheme. That used to be two strings typed into two systems — the
    /// platform half into an experience's config in the back office, the application half into a
    /// hand-written <c>AndroidManifest.xml</c> — with nothing checking they agreed, and a mismatch
    /// is silent: the intent simply finds no one.
    /// </para>
    /// <para>
    /// <b>There is nothing to agree on now.</b> Both halves derive the scheme from the app id, and
    /// it is the same GUID throughout: the HMAC <c>AppId</c> header is what the identity handler
    /// reads, what it puts in the token's <c>azp</c> claim, and what an <c>ExternalApp</c>
    /// experience is bound to. The tenant switch derives it from the credential in the app config
    /// a developer was given; the platform derives it from the app id the experience records.
    /// </para>
    /// <para>
    /// <b>No override, deliberately.</b> An override is a way to make the two halves disagree, and
    /// the failure it produces is the one this exists to prevent. If an application ever has to
    /// answer on a scheme of its own — one its users already have installed — that belongs on both
    /// sides at once, as something the platform reports and the switch reads, not as a local
    /// setting one side can hold alone.
    /// </para>
    /// <para>
    /// <b>Runtime and not editor-only</b> because both sides compute it: the tenant switch to write
    /// a manifest, the platform application to launch. Two copies of one derivation would be two
    /// things that can drift.
    /// </para>
    /// </remarks>
    public static class LaunchScheme
    {
        /// <summary>
        /// Android accepts rather more than this in a scheme, but a scheme that survives being
        /// typed, logged and read back over a call is worth more than one that is merely legal.
        /// </summary>
        private static readonly Regex valid = new("^[a-z][a-z0-9.+-]*$", RegexOptions.Compiled);

        /// <summary>
        /// <c>v</c> plus the app id, whole: <c>v</c> and then the GUID as anyone writes it.
        /// </summary>
        /// <remarks>
        /// The whole id rather than a piece of it, so the scheme cannot collide between two
        /// registered applications and so a reader can check it against the app id by eye. The
        /// dashes are legal — a URI scheme admits letters, digits, <c>+</c>, <c>-</c> and <c>.</c>
        /// after the first letter — and the leading <c>v</c> is there because a scheme may not
        /// start with a digit, which a GUID often does.
        /// </remarks>
        /// <returns>The scheme, or null when there is no app id to derive one from.</returns>
        public static string Derive(Guid? appId)
        {
            if (appId == null || appId == Guid.Empty)
            {
                return null;
            }

            return $"v{appId.Value:D}".ToLowerInvariant();
        }

        /// <summary>
        /// What the platform opens to launch the application bound to <paramref name="appObjectId"/>.
        /// </summary>
        /// <remarks>
        /// A prefix, not a whole address: the launcher appends its query to this, and the intent
        /// filter matches on the scheme alone — so an application free to answer on any host and
        /// path still answers here.
        /// </remarks>
        public static string UriFor(Guid? appObjectId)
        {
            string scheme = Derive(appObjectId);

            return string.IsNullOrEmpty(scheme) ? null : $"{scheme}://";
        }

        /// <summary>Whether a scheme can be put in a manifest as it stands.</summary>
        public static bool IsValid(string scheme)
            => !string.IsNullOrEmpty(scheme) && valid.IsMatch(scheme);
    }
}
