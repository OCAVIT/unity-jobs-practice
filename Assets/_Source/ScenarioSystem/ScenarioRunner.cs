using System;
using System.Linq;
using OrbitSystem;
using Unity.Profiling;
using UnityEngine;

namespace ScenarioSystem
{
    /// <summary>Проигрывает сценарии и метит их кадры, чтобы анализ брал только нужный отрезок записи.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class ScenarioRunner : MonoBehaviour
    {
        public const string FrameMarkerName = "ProfilerLab.ScenarioFrame";

        private static readonly ProfilerMarker FrameMarker = new(FrameMarkerName);

        private Scenario _scenario;
        private float _duration;
        private float _elapsed;
        private int _frame;
        private bool _begun;

        public static ScenarioRunner Instance { get; private set; }
        public event Action<string> Finished;
        public string LastFinished { get; private set; }
        public bool IsBusy => _scenario != null;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
#if !UNITY_EDITOR
            ScenarioLink.ListenInPlayer(this);
#endif
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Run(string id, float duration = 0f)
        {
            _scenario = ScenarioCatalog.All.First(scenario => scenario.Id == id);
            _duration = duration > 0f ? duration : _scenario.Duration;
            _elapsed = 0f;
            _frame = 0;
            _begun = false;
            LastFinished = null;
        }

        private void Update()
        {
            if (_scenario == null) return;

            using (FrameMarker.Auto()) { }

            var field = OrbitField.Current;
            if (field == null) return;

            if (_begun) _elapsed += Time.unscaledDeltaTime;
            var context = new ScenarioContext(field, _elapsed, _frame);
            if (!_begun)
            {
                _begun = true;
                _scenario.Begin?.Invoke(context);
            }

            _scenario.Step?.Invoke(context);
            _frame++;

            if (_elapsed >= _duration) Finish(field);
        }

        private void Finish(OrbitField field)
        {
            field.Configure(MotionMode.Jobs, OutputMode.None, LogMode.Off);
            var id = _scenario.Id;
            _scenario = null;
            LastFinished = id;
            Finished?.Invoke(id);
        }
    }
}
