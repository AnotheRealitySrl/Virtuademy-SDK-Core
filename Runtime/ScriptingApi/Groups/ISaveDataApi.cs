namespace Virtuademy.ScriptingApi
{
    /// <summary>
    /// The local player's own saved values, and the leaderboards a world writes to.
    /// </summary>
    public interface ISaveDataApi
    {
        /// <summary>
        /// The value saved under <paramref name="key"/> for the local player, or null when there is
        /// none. Typed <c>object</c> because that is what the platform stores — a script casts.
        /// </summary>
        /// <remarks>Node: <c>Virtuademy Player Save Data: Get Data</c>.</remarks>
        object Get(string key);

        /// <summary>Saves a value under <paramref name="key"/> for the local player.</summary>
        /// <remarks>Node: <c>Virtuademy Player Save Data: Set Data</c>.</remarks>
        void Set(string key, object value);

        /// <summary>Removes the local player's value for <paramref name="key"/>.</summary>
        /// <remarks>Node: <c>Virtuademy Player Save Data: Delete Data</c>.</remarks>
        void Delete(string key);

        /// <summary>
        /// Submits a score for the local player to the leaderboard named
        /// <paramref name="leaderboardKey"/>.
        /// </summary>
        /// <remarks>
        /// Node: <c>Virtuademy Leaderboard Create Record: Create Record</c>, which builds the record
        /// out of exactly these two values.
        /// </remarks>
        void SubmitLeaderboardRecord(string leaderboardKey, float value);
    }
}
