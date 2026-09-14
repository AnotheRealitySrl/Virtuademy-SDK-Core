namespace Virtuademy.ScriptingApi
{
    public abstract class ExperienceStepDTO : ExperienceAnalyticDTO
    {
        [SettableField(isRequired = true)]
        public string stepId;
    }
}
