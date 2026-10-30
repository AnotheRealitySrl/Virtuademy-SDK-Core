using System;

namespace Virtuademy.ScriptingApi
{
    [Serializable]
    public class ExperienceStepCompleteDTO : ExperienceStepDTO
    {
        [SettableField]
        public int? score;
        [SettableField]
        public int? maxScore;
        [SettableField]
        public int? passingScore;
        [SettableField]
        public EExperienceOutcome outcome;
        [SettableField]
        public EExperienceScoringType scoringType;
        [SettableField]
        public string notes;

        public ExperienceStepCompleteDTO()
        {
        }

        /// <summary>A step's result: plain ints (an interpreted script need not touch <c>Nullable&lt;int&gt;</c>).</summary>
        public ExperienceStepCompleteDTO(string key, string stepId, int score, int maxScore, int passingScore,
            EExperienceOutcome outcome, EExperienceScoringType scoringType)
        {
            SetKey(key);
            this.stepId = stepId;
            this.score = score;
            this.maxScore = maxScore;
            this.passingScore = passingScore;
            this.outcome = outcome;
            this.scoringType = scoringType;
        }
    }
}
