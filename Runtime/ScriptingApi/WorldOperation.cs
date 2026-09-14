using UnityEngine;

namespace Virtuademy.ScriptingApi
{
    /// <summary>
    /// Something the platform is doing that a script can wait for, by yielding on it in a coroutine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists at all.</b> The nodes these members replaced were awaitable: the flow
    /// stopped until the camera arrived or the experience was found. A script could not express
    /// that, because the whitelist denies <c>System.Threading</c> and <c>Task</c> is caught by
    /// that prefix. A coroutine can — <c>StartCoroutine(IEnumerator)</c> passes the whitelist,
    /// only dispatching a coroutine <i>by name</i> is refused — so the handle is a
    /// <see cref="CustomYieldInstruction"/> and a script writes <c>yield return</c>.
    /// </para>
    /// <para>
    /// <b>It carries timing, not results.</b> Whatever the operation produces still arrives through
    /// its callback, and this says only when it is finished. That is deliberate: a handle per
    /// result type would mean either a family of near-identical types or a generic one, and a
    /// generic instantiated only in interpreted code has no ahead-of-time counterpart to run
    /// against. So the two concerns stay apart, and the shape a script writes is:
    /// <code>
    /// ExperienceView found = null;
    /// yield return IVirtuademyFramework.Current.Session.FindExperience("atrium", e => found = e);
    /// </code>
    /// </para>
    /// <para>
    /// <b>Ignoring it is fine and is the common case.</b> Every one of these members worked without
    /// a return value before this existed; a call that does not care when the work finishes reads
    /// exactly as it did.
    /// </para>
    /// </remarks>
    public class WorldOperation : CustomYieldInstruction
    {
        /// <summary>Whether the platform has finished.</summary>
        public bool IsDone { get; private set; }

        /// <inheritdoc/>
        public override bool keepWaiting => !IsDone;

        /// <summary>
        /// Marks the operation finished. Internal: only the application that started the work can
        /// say it is over, and a script that could would be able to release every coroutine
        /// waiting on it.
        /// </summary>
        internal void Complete() => IsDone = true;

        /// <summary>An operation that was already over when it was handed back.</summary>
        /// <remarks>
        /// For the members that have nothing to wait for on some paths — a null argument rejected
        /// before anything starts, a system that is not installed. Yielding on it costs one frame
        /// at most, which is what yielding on any completed instruction costs.
        /// </remarks>
        internal static WorldOperation Completed()
        {
            WorldOperation operation = new WorldOperation();
            operation.Complete();

            return operation;
        }
    }
}
