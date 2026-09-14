using System;

namespace Virtuademy.ScriptingApi
{
    /// <summary>
    /// What only the platform knows, and a script could not work out for itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The line is server-held state, not audience.</b> Which session this is, what the local
    /// player has saved, what this run reports about the learner — none of it exists anywhere but
    /// the platform, so nothing else could answer. Everything the Virtuademy player merely
    /// <i>provides</i> — the avatar, the world, the screen, the help panel, the language, the
    /// device — is on <c>IVirtuademyGameplay</c>.
    /// </para>
    /// <para>
    /// <b>This is not the surface an external application uses.</b> It was described that way at
    /// first and the code never supported it: <see cref="Install"/> is internal and opened to one
    /// assembly, the platform application's own, so nothing else can put an implementation behind
    /// either interface. An external app reaches the platform through the HTTP and realtime
    /// clients in <c>Virtuademy-SDK-Library</c>, where <c>Task</c> is unconstrained because the
    /// whitelist binds creator code only. What this assembly genuinely shares with that path is
    /// its <i>types</i> — the views and the analytic statements — which is why they live here and
    /// why it declares no first-party reference.
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

        /// <summary>The local player's own saved values, and the leaderboards.</summary>
        ISaveDataApi SaveData { get; }

        /// <summary>What this run reports about what the learner did.</summary>
        IAnalyticsApi Analytics { get; }
    }
}
