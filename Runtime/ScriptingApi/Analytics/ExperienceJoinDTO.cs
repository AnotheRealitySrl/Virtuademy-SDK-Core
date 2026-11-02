using System;

namespace Virtuademy.ScriptingApi
{
    [Serializable]
    public class ExperienceJoinDTO : ExperienceAnalyticDTO
    {
        [SettableField(isRequired = true)]
        public string context;

        public ExperienceJoinDTO()
        {
        }

        public ExperienceJoinDTO(string key, string context)
        {
            SetKey(key);
            this.context = context;
        }

    }
}
