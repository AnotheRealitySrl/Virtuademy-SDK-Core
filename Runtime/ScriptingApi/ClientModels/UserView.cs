using System;
using System.Collections.Generic;

using UnityEngine;

namespace Virtuademy.ScriptingApi
{
    /// <summary>
    /// A user as an authored world may see one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the trimmed half of the client model, not a copy of it.</b> The application's
    /// <c>CMUser</c> derives from this and adds what only the application needs. Splitting it here
    /// is what lets a creator's project hold the five values the shipped nodes expose without also
    /// holding the platform's notion of a user account.
    /// </para>
    /// <para>
    /// <b>Why the profile image is a string and the preferences are absent.</b> The one thing
    /// anything reads through <c>CMUserPreference</c> is the avatar's PNG, which the
    /// <c>Expose: CMUser</c> node already flattens into a single <c>ProfileImageURL</c> port.
    /// Carrying the preference object across would hand an authored world the user's nickname, bio,
    /// height, date of birth, city, social links and hand preference as well. That is personal data,
    /// and the surface exists to decide questions like this one deliberately.
    /// </para>
    /// </remarks>
    [Serializable]
    public class UserView
    {
        [SerializeField] private int id;
        [SerializeField] private string displayName;
        [SerializeField] private string email;
        [SerializeField] private string profileImageUrl;
        [SerializeField] private List<TagView> tags = new();

        /// <summary>The platform's identifier for this user.</summary>
        public int Id { get => id; set => id = value; }

        /// <summary>The name to put on screen. Never the account's login.</summary>
        public virtual string DisplayName { get => displayName; set => displayName = value; }

        public string Email { get => email; set => email = value; }

        /// <summary>The avatar's rendered image, or null when the user has none.</summary>
        public virtual string ProfileImageUrl { get => profileImageUrl; set => profileImageUrl = value; }

        /// <summary>
        /// The user's tags. Populated with the application's richer tag type, which derives from
        /// <see cref="TagView"/>, so an authored world reads the four values it needs and the
        /// application still has the rest.
        /// </summary>
        public List<TagView> Tags { get => tags; set => tags = value; }
    }
}
