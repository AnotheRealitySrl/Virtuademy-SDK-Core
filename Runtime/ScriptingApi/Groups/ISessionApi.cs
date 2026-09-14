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
        /// <remarks>Node: <c>Reflectis Networking: Get Local Player ID</c>.</remarks>
        string SessionId { get; }

        /// <summary>Whether other people can be in this session at all.</summary>
        bool IsMultiplayer { get; }

        /// <summary>
        /// Whether this client is the one the others follow for authoritative decisions. False in a
        /// single-player session, where there is nobody to be master of.
        /// </summary>
        /// <remarks>Node: <c>Reflectis Network: IsMaster</c>.</remarks>
        bool IsMasterClient { get; }

        /// <summary>
        /// A clock every client in the session agrees on, for anything that has to look
        /// simultaneous. Falls back to local time when the world is offline or the session is not
        /// multiplayer, so it is always safe to read and only meaningful to *compare* between
        /// clients when <see cref="IsMultiplayer"/> is true.
        /// </summary>
        /// <remarks>Node: <c>Reflectis Networking: Get current network time</c>.</remarks>
        double NetworkTime { get; }

        /// <summary>The addressable name of the environment being played.</summary>
        string EnvironmentName { get; }

        /// <summary>Whether the environment was authored as multiplayer.</summary>
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
        /// <remarks>Node: <c>Reflectis Networking: Get Current Shard Open State</c>.</remarks>
        bool IsShardOpen { get; }

        /// <summary>Opens or closes the local player's shard to newcomers.</summary>
        /// <remarks>Node: <c>Reflectis Networking: Set Current Shard Open State</c>.</remarks>
        void SetShardOpen(bool open);

        /// <summary>The local player's platform id.</summary>
        int LocalUserId { get; }

        /// <summary>
        /// The local player's display name. Empty for a user who has never saved one, which the
        /// platform's own model represents as an absent preference rather than a blank.
        /// </summary>
        string LocalUserName { get; }
    }
}
