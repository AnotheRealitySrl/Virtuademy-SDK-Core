using System;

using UnityEngine;

namespace Virtuademy.ScriptingApi
{
    /// <summary>
    /// A tag as an authored world may see one: what it says, how it looks, whether it shows.
    /// </summary>
    /// <remarks>
    /// The application's <c>CMTag</c> derives from this and keeps the bookkeeping — creation and
    /// update stamps, the internal note, the enabled flag and the tag's kind — which an authored
    /// world has no use for and the platform would rather not publish.
    /// </remarks>
    [Serializable]
    public class TagView
    {
        [SerializeField] private int id;
        [SerializeField] private string label;
        [SerializeField] private Color color;
        [SerializeField] private bool visible;

        public int Id { get => id; set => id = value; }

        public string Label { get => label; set => label = value; }

        /// <summary>The colour the platform assigns this tag, for badges and name plates.</summary>
        public Color Color { get => color; set => color = value; }

        /// <summary>Whether this tag is meant to be shown at all.</summary>
        public bool Visible { get => visible; set => visible = value; }
    }
}
