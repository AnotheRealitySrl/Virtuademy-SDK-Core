using System;

using System.Collections;

namespace Virtuademy.ScriptingApi
{
    /// <summary>
    /// What a world reports about what the learner did, as xAPI statements.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The flat contract had no group for this — the two members sat among the session and camera
    /// ones — which is why the analytics nodes were the only nodes in the package that reached past
    /// their own area to find them.
    /// </para>
    /// <para>
    /// The statement types live in this assembly beside this interface, so an external application
    /// that reports its own activity gets them without installing the environment authoring tools,
    /// and an interpreted script can name them.
    /// </para>
    /// </remarks>
    public interface IAnalyticsApi
    {
        /// <summary>
        /// Reports one statement. The verb decides which payload shape the platform expects, and
        /// the node that raises this builds the matching one.
        /// </summary>
        /// <remarks>Node: <c>Analytic: Send Data</c>.</remarks>
        void Send(EAnalyticVerb verb, AnalyticDTO analytic);

        /// <summary>
        /// Sends <paramref name="analytic"/> and calls <paramref name="onSent"/> when the platform answered: success, or
        /// failure with what went wrong. Unlike the overload without it, a failure does not throw: the caller learns it
        /// and can send again (a queue with retries).
        /// </summary>
        void Send(EAnalyticVerb verb, AnalyticDTO analytic, AnalyticSentHandler onSent);

        /// <summary>
        /// Mints the experience id every later statement in this run is grouped under, and calls
        /// <paramref name="onReady"/> once the platform has it.
        /// </summary>
        /// <remarks>
        /// Nothing a world sends before this lands can be attributed to the run, so the node that
        /// raises this is the one the graph waits on. Node: <c>Analytic: Generate Experience ID</c>.
        /// </remarks>
        IEnumerator GenerateExperienceGuid(string key, Action onReady = null);
    }
}
