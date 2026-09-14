using Virtuademy.SDK.Core.Utilities;

using UnityEngine;

using SPACS.Utilities;

namespace Virtuademy.SDK.Core.ApiSystem
{
    /// <summary>
    /// The HMAC credential this build signs its pre-login calls with, written by the editor tooling
    /// at tenant switch. See ADR 0025 in the meta-repo.
    /// <para>
    /// <b>This asset is a secret and must not be committed.</b> It is deliberately separate from
    /// <see cref="PlatformEndpoints"/>, which carries the same generation but no credential and is
    /// committed: with one asset holding both, a git history cannot distinguish an environment
    /// moving hostname from a credential being rotated, and ignoring the file to protect the secret
    /// would hide the half worth reviewing. Two assets, one boundary, both visible for what they
    /// are.
    /// </para>
    /// <para>
    /// It replaces the credential being stamped into each API system's own asset by the tenant-switch
    /// configurator — 18 of those were committed with a live secret, across 3 app registrations, and
    /// one of those registration ids was found inside a shipped WebGL data file. Consolidating to
    /// one asset reduces the copies and the review surface; it does <b>not</b> stop the credential
    /// reaching the player, which is a separate decision about how a client obtains its first token.
    /// </para>
    /// <para>
    /// No <c>[CreateAssetMenu]</c>: the asset is generated. One made by hand would be empty and
    /// would shadow the generated one.
    /// </para>
    /// </summary>
    public class PlatformCredentials : ScriptableObject
    {
        [SerializeField] private HmacCredential credential;

        /// <summary>
        /// Null-safe: an absent or ungenerated asset yields no credential rather than throwing, so
        /// a project that has never run the generator fails at the call that needs it — with a
        /// message about the credential — instead of at load.
        /// </summary>
        public HmacCredential Credential => credential;

        public bool HasCredential =>
            credential != null
            && credential.AppId != System.Guid.Empty
            && !string.IsNullOrEmpty(credential.AppSecret);

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only writer, called by the generator. Refuses an incomplete credential for the
        /// same reason <see cref="PlatformEndpoints.Write"/> refuses an empty set: a failed or
        /// partial fetch must not overwrite a working one.
        /// </summary>
        public bool Write(HmacCredential incoming)
        {
            if (incoming == null || incoming.AppId == System.Guid.Empty || string.IsNullOrEmpty(incoming.AppSecret))
            {
                return false;
            }

            credential = incoming;
            return true;
        }
#endif
    }
}
