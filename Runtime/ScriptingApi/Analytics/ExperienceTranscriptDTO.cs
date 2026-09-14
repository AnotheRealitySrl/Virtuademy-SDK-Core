using System;

namespace Virtuademy.ScriptingApi
{
    [Serializable]
    public class ExperienceTranscriptDTO : ExperienceStepDTO
    {
        [SettableField(isRequired = true)]
        public string description;
    }
}
