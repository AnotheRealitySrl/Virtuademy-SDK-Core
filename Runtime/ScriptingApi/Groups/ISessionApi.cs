using System;

using System.Collections;

namespace Virtuademy.ScriptingApi
{
    /// <summary>
    /// Facts about the session and the environment the world is running in, and the one thing a
    /// world may change about them: whether its shard accepts newcomers.
    /// </summary>
    /// <remarks>
    /// The platform's own models — <c>CMSession</c>, <c>CMEnvironment</c>, <c>CMUser</c> — cannot
    /// cross this boundary, so what a script reads is the handful of values the nodes actually took
    /// out of them.
    /// </remarks>
    public interface ISessionApi
    {
        /// <summary>
        /// The platform's id for the session, or empty outside one — a world opened from the editor,
        /// for instance.
        /// </summary>
        /// <remarks>Node: <c>Virtuademy Networking: Get Local Player ID</c>.</remarks>
        string SessionId { get; }

        /// <summary>Whether other people can be in this session at all.</summary>
        bool IsMultiplayer { get; }

        /// <summary>
        /// Whether this client is the one the others follow for authoritative decisions. True in a
        /// single-player session, where there is nobody other than the current player.
        /// </summary>
        /// <remarks>Node: <c>Virtuademy Network: IsMaster</c>.</remarks>
        bool IsMasterClient { get; }

        /// <summary>
        /// A clock every client in the session agrees on, for anything that has to look
        /// simultaneous. Falls back to local time when the world is offline or the session is not
        /// multiplayer, so it is always safe to read and only meaningful to *compare* between
        /// clients when <see cref="IsMultiplayer"/> is true.
        /// </summary>
        /// <remarks>Node: <c>Virtuademy Networking: Get current network time</c>.</remarks>
        double NetworkTime { get; }

        /// <summary>The addressable name of the environment being played.</summary>
        string EnvironmentName { get; }

        /// <summary>Obsolete: use <see cref="IsMultiplayer"/>, the mode of the current session.</summary>
        // Kept, answering false as it always did, for the scripts already published against it: they
        // would fail on the call if it were removed. The platform records no multiplayer flag on an
        // environment; the Reload Scene node, which read it, calls ReloadSession now.
        [Obsolete("Use IsMultiplayer, the mode of the current session.")]
        bool IsEnvironmentMultiplayer { get; }

        /// <summary>
        /// Whether the local player is in a shard at all. Checked before
        /// <see cref="IsShardOpen"/>, which cannot express "there is no shard" — a
        /// <c>bool?</c> would, and nullable types are one of the things this surface avoids.
        /// </summary>
        bool HasShard { get; }

        /// <summary>
        /// Whether the shard accepts newcomers. Meaningless when <see cref="HasShard"/> is false.
        /// </summary>
        /// <remarks>Node: <c>Virtuademy Networking: Get Current Shard Open State</c>.</remarks>
        bool IsShardOpen { get; }

        /// <summary>Opens or closes the local player's shard to newcomers.</summary>
        /// <remarks>Node: <c>Virtuademy Networking: Set Current Shard Open State</c>.</remarks>
        void SetShardOpen(bool open);

        /// <summary>The local player's platform id.</summary>
        int LocalUserId { get; }

        /// <summary>
        /// The local player's display name. Empty for a user who has never saved one, which the
        /// platform's own model represents as an absent preference rather than a blank.
        /// </summary>
        string LocalUserName { get; }
    
        /// <summary>
        /// The local player as a whole, for the graph that exposes the user rather than reading
        /// their name or id out of it.
        /// </summary>
        /// <remarks>Node: <c>Expose: CMUser</c>.</remarks>
        UserView LocalUser { get; }

        /// <summary>
        /// The session as a whole, for the graph that exposes it rather than reading one value out
        /// of it. The scalars above stay because that is what almost every node wants, and going
        /// through a view to ask whether the session is multiplayer would be worse on both sides.
        /// </summary>
        /// <remarks>Node: <c>Expose: CMSession</c>.</remarks>
        SessionView Details { get; }

        /// <summary>The environment as a whole, for the same reason as <see cref="Details"/>.</summary>
        /// <remarks>Node: <c>Expose: CMEnvironment</c>.</remarks>
        EnvironmentView Environment { get; }

        /// <summary>
        /// Looks a user up by platform id and hands the result to <paramref name="onFound"/>, or
        /// hands null when there is no such user. A callback rather than a returned task because a
        /// script cannot await one: the whitelist denies <c>System.Threading</c>.
        /// </summary>
        /// <remarks>Node: <c>Virtuademy: Get CMUser by ID</c>.</remarks>
        IEnumerator GetUser(int userId, Action<UserView> onFound);

        /// <summary>
        /// Looks an experience up by the addressable name of its environment. Hands null when the
        /// platform has no experience for that name, which is how a world checks whether a scene it
        /// wants to send the player to exists at all.
        /// </summary>
        /// <remarks>Nodes: <c>Change Scene</c>, <c>Check Scene Availability</c>.</remarks>
        IEnumerator FindExperience(string addressableName, Action<ExperienceView> onFound);

        /// <summary>
        /// Sends the local player into another experience. <paramref name="onJoined"/> receives
        /// whether the platform accepted; on success the current world is already being torn down
        /// by the time it runs, so there is rarely anything useful left to do in it.
        /// </summary>
        /// <remarks>Node: <c>Change Scene</c>.</remarks>
        IEnumerator JoinExperience(ExperienceView experience, bool multiplayer, Action<bool> onJoined = null);

        /// <summary>
        /// Plays the current experience again, in the current session's mode: a multiplayer session
        /// is joined again, in the same shard, with the same people and the state they share; a
        /// single-player one starts over from the experience's initial state.
        /// <paramref name="onReloaded"/> receives whether the platform accepted; as with
        /// <see cref="JoinExperience"/>, on success the world is already being torn down by the time
        /// it runs.
        /// </summary>
        /// <remarks>Node: <c>Reload Scene</c>.</remarks>
        IEnumerator ReloadSession(Action<bool> onReloaded = null);

        /// <summary>
        /// Someone else joined the session: their platform id and their session id.
        /// </summary>
        /// <remarks>
        /// The platform's own <c>PlayerData</c> does not cross: the node that raises this has
        /// exactly two ports, <c>UserId</c> and <c>SessionId</c>, and pulled both out of the payload
        /// on its first line. Node: <c>Virtuademy Networking: On Other Player Entered</c>.
        /// </remarks>
        event Action<int, string> OtherPlayerEntered;

        /// <summary>Someone else left the session. Same two values as <see cref="OtherPlayerEntered"/>.</summary>
        /// <remarks>Node: <c>Virtuademy Networking: On Other Player Left</c>.</remarks>
        event Action<int, string> OtherPlayerLeft;
}
}
