using System;

namespace Virtuademy.ScriptingApi
{
    /// <summary>
    /// What an authored world and an external app can both ask of the platform.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The half that does not depend on being a world.</b> Session facts, language, the local
    /// player's saved values, which device this is, fading the view, the help panel — an external
    /// app embedded in Virtuademy needs every one of these, and so does an authored environment.
    /// What only an environment has — the avatar rig, the placeholders, the spawned objects, the
    /// ownership of synced objects — lives on <c>IVirtuademyGameplay</c>, in the package a creator
    /// installs and an external app does not.
    /// </para>
    /// <para>
    /// <b>The statics live here rather than on a separate locator class.</b> A Visual Scripting node
    /// is built by the graph and an interpreted script has no injection point, so reaching the
    /// platform has to be static; putting that on the contract itself means one type instead of two,
    /// and it is the shape <c>IApplicationManager</c> already used before this.
    /// </para>
    /// <para>
    /// <b>Written to the interpreter's budget</b>, which is what lets a script name it at all: no
    /// <c>Task</c> — <c>System.Threading</c> is denied — no generic member, no <c>Nullable&lt;T&gt;</c>,
    /// and no type from a namespace the whitelist refuses. That is why the groups hand back views
    /// rather than the application's client models, and why anything asynchronous takes an
    /// <see cref="Action"/>.
    /// </para>
    /// </remarks>
    public interface IVirtuademyFramework
    {
        private static IVirtuademyFramework current;

        /// <summary>
        /// Whether the platform is behind this surface. False in an authoring project, where the
        /// contracts compile but nothing answers them.
        /// </summary>
        static bool IsAvailable => current != null;

        /// <summary>The platform this world or app is running in.</summary>
        /// <exception cref="InvalidOperationException">
        /// Nothing has installed an implementation. Check <see cref="IsAvailable"/> first if the
        /// code can run outside the platform.
        /// </exception>
        static IVirtuademyFramework Current => current ?? throw new InvalidOperationException(
            "No Virtuademy framework is installed, so this code cannot reach the platform. A "
            + "Virtuademy application installs one before the first scene loads; check "
            + "IVirtuademyFramework.IsAvailable if it can run outside one.");

        /// <summary>Raised when an implementation is installed, for code that initialised first.</summary>
        static event Action Installed;

        /// <summary>
        /// Registers the application's implementation. Internal by design: a script references this
        /// assembly in full, and a public installer would let one script replace the surface every
        /// other script is calling.
        /// </summary>
        internal static void Install(IVirtuademyFramework framework)
        {
            current = framework ?? throw new ArgumentNullException(nameof(framework));

            Installed?.Invoke();
        }

        /// <summary>Read-only facts about the session this is running in.</summary>
        ISessionApi Session { get; }

        /// <summary>The active language and the strings authored against it.</summary>
        ILocalizationApi Localization { get; }

        /// <summary>The local player's own saved values, and the leaderboards.</summary>
        ISaveDataApi SaveData { get; }

        /// <summary>Which kind of device this is running on.</summary>
        IPlatformApi Platform { get; }

        /// <summary>Fading the view in and out.</summary>
        IScreenApi Screen { get; }

        /// <summary>The help panel, when the host provides one.</summary>
        IHelpApi Help { get; }

        /// <summary>What this run reports about what the learner did.</summary>
        IAnalyticsApi Analytics { get; }
    }
}
