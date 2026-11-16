using System.Runtime.Serialization;

namespace Virtuademy.ScriptingApi
{
    public enum EExperienceScoringType
    {
        None = 0,
        Numeric,
        Stars,
        // On the wire (StringEnumConverter) the server's name: `Percent`
        [EnumMember(Value = "Percent")]
        Percentage
    }
}
