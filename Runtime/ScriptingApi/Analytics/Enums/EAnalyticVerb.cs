using System.Runtime.Serialization;

namespace Virtuademy.ScriptingApi
{
    public enum EAnalyticVerb
    {
        ExpJoin = 1,
        ExpStart,
        ExpComplete,
        StepStart,
        StepComplete,
        // On the wire (StringEnumConverter) the server's name: `Transcript`
        [EnumMember(Value = "Transcript")]
        ExpTranscript
    }
}
