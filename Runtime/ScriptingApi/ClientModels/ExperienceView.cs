using System;

using UnityEngine;

namespace Virtuademy.ScriptingApi
{
    /// <summary>
    /// The experience an authored world is running inside: what it is called and what it says it is.
    /// </summary>
    /// <remarks>
    /// The three values the <c>Expose: CMSession</c> node reaches through <c>Session.Experience</c>.
    /// The application's <c>CMExperience</c> derives from this and keeps ownership, draft and
    /// publication state, the config blob, the thumbnail and the rest, none of which an authored
    /// world can act on.
    /// </remarks>
    [Serializable]
    public class ExperienceView
    {
        [SerializeField] private int id;
        [SerializeField] private string title;
        [SerializeField] private string description;

        public int Id { get => id; set => id = value; }

        public string Title { get => title; set => title = value; }

        public string Description { get => description; set => description = value; }
    }
}
