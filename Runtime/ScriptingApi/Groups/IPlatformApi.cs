namespace Virtuademy.ScriptingApi
{
    /// <summary>
    /// Which kind of device the world is running on.
    /// </summary>
    /// <remarks>
    /// Three booleans rather than an enum. The platform has one — <c>ESupportedPlatform</c> — but it
    /// belongs to the authoring package and cannot cross this boundary, and declaring a second enum
    /// here would be two lists to keep in step for the sake of three values.
    /// <para>Node: <c>Reflectis Platform: Switch</c>, which branches on the same three.</para>
    /// </remarks>
    public interface IPlatformApi
    {
        /// <summary>Running in a headset.</summary>
        bool IsVR { get; }

        /// <summary>Running in a browser.</summary>
        bool IsWebGL { get; }

        /// <summary>Running on a phone or tablet.</summary>
        bool IsMobile { get; }
    }
}
