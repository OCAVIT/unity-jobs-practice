using System;

namespace ScenarioSystem
{
    public sealed class Scenario
    {
        public const float DefaultDuration = 4.5f;

        public string Id { get; }
        public string Title { get; }
        public Action<ScenarioContext> Begin { get; }
        public Action<ScenarioContext> Step { get; }
        public float Duration { get; }
        public bool PlayerOnly { get; }
        public bool DeepOnly { get; }

        /// <summary>Часть имени маркера джоба, который нужно найти на рабочих потоках и выделить на снимке.</summary>
        public string Focus { get; }

        public Scenario(string id, string title, Action<ScenarioContext> begin = null, Action<ScenarioContext> step = null, float duration = DefaultDuration, bool playerOnly = false, bool deepOnly = false, string focus = null)
        {
            Id = id;
            Title = title;
            Begin = begin;
            Step = step;
            Duration = duration;
            PlayerOnly = playerOnly;
            DeepOnly = deepOnly;
            Focus = focus;
        }
    }
}
