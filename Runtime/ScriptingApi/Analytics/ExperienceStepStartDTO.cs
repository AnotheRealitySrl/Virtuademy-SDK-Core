using System;

namespace Virtuademy.ScriptingApi
{
    [Serializable]
    public class ExperienceStepStartDTO : ExperienceStepDTO
    {
        [SettableField(isRequired = true)]
        public string description;

        public ExperienceStepStartDTO()
        {
        }

        public ExperienceStepStartDTO(string key, string stepId, string description)
        {
            SetKey(key);
            this.stepId = stepId;
            this.description = description;
        }
    }
}
