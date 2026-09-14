using System;

namespace Virtuademy.ScriptingApi
{
    [Serializable]
    public class ExperienceStepStartDTO : ExperienceStepDTO
    {
        [SettableField(isRequired = true)]
        public string description;
    }
}
