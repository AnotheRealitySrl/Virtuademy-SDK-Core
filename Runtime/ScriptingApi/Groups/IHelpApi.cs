using System;

namespace Virtuademy.ScriptingApi
{
    /// <summary>
    /// The help panel, for a world that wants to open or close it as part of its own flow.
    /// </summary>
    public interface IHelpApi
    {
        /// <summary>
        /// Whether the application provides a help panel at all. A world can run in a host that has
        /// none, so this is worth checking before the rest.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>Opens the help panel.</summary>
        /// <remarks>Node: <c>Reflectis Tutorial: Enable</c>.</remarks>
        void Open();

        /// <summary>Closes it.</summary>
        void Close();

        /// <summary>Raised once the panel has finished closing.</summary>
        /// <remarks>Node: <c>Reflectis Tutorial: On Tutorial Closed</c>.</remarks>
        event Action Closed;
    }
}
