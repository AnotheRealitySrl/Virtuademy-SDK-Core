using System;
using System.Collections.Generic;

using UnityEngine;

namespace Virtuademy.ScriptingApi
{
    /// <summary>
    /// The session an authored world is running in: when it runs, who may see it, what it is for.
    /// </summary>
    /// <remarks>
    /// The six values the <c>Expose: CMSession</c> node publishes, two of them reached through
    /// <see cref="Experience"/>. The application's <c>CMSession</c> derives from this and keeps
    /// participants, capacity, permissions, ownership, the short link and the rest of what running
    /// a session takes.
    /// </remarks>
    [Serializable]
    public class SessionView
    {
        [SerializeField] private int id;
        [SerializeField] private bool isPublic;
        [SerializeField] private ExperienceView experience;
        [SerializeField] private List<TagView> tags = new();

        private DateTime? startDateTime;
        private DateTime? endDateTime;

        public int Id { get => id; set => id = value; }

        /// <summary>When the session opens, or null when it is not scheduled.</summary>
        public DateTime? StartDateTime { get => startDateTime; set => startDateTime = value; }

        /// <summary>When the session closes, or null when it does not.</summary>
        public DateTime? EndDateTime { get => endDateTime; set => endDateTime = value; }

        /// <summary>Whether anyone may join, as opposed to an invited list.</summary>
        public bool IsPublic { get => isPublic; set => isPublic = value; }

        public ExperienceView Experience { get => experience; set => experience = value; }

        public List<TagView> Tags { get => tags; set => tags = value; }
    }
}
