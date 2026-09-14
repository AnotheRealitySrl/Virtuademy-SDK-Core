using System;

using UnityEngine;

namespace Virtuademy.ScriptingApi
{
    /// <summary>
    /// The environment an authored world is: its name, its description, and how it is loaded.
    /// </summary>
    /// <remarks>
    /// The five values the <c>Expose: CMEnvironment</c> node publishes. The application's
    /// <c>CMEnvironment</c> derives from this and keeps the catalogue it is served from, the loaded thumbnail texture, the tenant
    /// flag, the localisation sources, the supported-platform list and the tags — the things the
    /// platform's own catalogue UI needs and an authored world does not.
    /// </remarks>
    [Serializable]
    public class EnvironmentView
    {
        [SerializeField] private int id;
        [SerializeField] private string name;
        [SerializeField] private string description;
        [SerializeField] private string addressableKey;

        /// <summary>
        /// The platform's identifier for this environment. Spelled <c>ID</c> rather than <c>Id</c>
        /// because the client model spelled it that way and a creator's graph records the member's
        /// name: changing the case would break every graph that reads it, for nothing.
        /// </summary>
        public int ID { get => id; set => id = value; }

        public string Name { get => name; set => name = value; }

        public string Description { get => description; set => description = value; }

        /// <summary>The key this environment's content is loaded by.</summary>
        public string AddressableKey { get => addressableKey; set => addressableKey = value; }

    }
}
