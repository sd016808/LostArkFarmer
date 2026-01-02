using System.Collections.Generic;

namespace LostArkAutoPlayer.Models
{
    public class ScriptConfig
    {
        public int LoopDelayMs { get; set; }
        public bool EnableAutoReturn { get; set; } = false;
        public System.Collections.Generic.List<SkillStep> Skills { get; set; }
    }

    public class SkillStep
    {
        public string Note { get; set; }
        public List<string> Buttons { get; set; }
        public int PressDurationMs { get; set; }
        public int CoolDownMs { get; set; }

        // 預設 true (依序按)，但在移動時設為 false (同時按)
        public bool IsSequential { get; set; } = true;
    }
}