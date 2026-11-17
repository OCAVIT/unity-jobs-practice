using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Jobs;

namespace OrbitSystem
{
    /// <summary>
    /// Спавнит объекты при старте и каждый кадр двигает их по кругу: на главном потоке или через IJobParallelForTransform.
    /// Часть 2: раз в N секунд для каждого объекта считает логарифм случайного числа через IJob.
    /// </summary>
    public sealed class OrbitField : MonoBehaviour
    {
        private struct PendingLog
        {
            public JobHandle Handle;
            public NativeArray<float> Result;
            public int Index;
            public int Value;
        }

        private static readonly ProfilerMarker MainThreadMarker = new("Orbit.MoveOnMainThread");
        private static readonly ProfilerMarker ScheduleMoveMarker = new("Orbit.ScheduleMoveJob");
        private static readonly ProfilerMarker ScheduleLogsMarker = new("Orbit.ScheduleLogJobs");
        private static readonly ProfilerMarker CompleteMarker = new("Orbit.CompleteJobs");
        private static readonly ProfilerMarker LogOutputMarker = new("Orbit.OutputToLog");
        private static readonly ProfilerMarker ScreenOutputMarker = new("Orbit.OutputToScreen");

        [SerializeField] private GameObject orbiterPrefab;
        [SerializeField] private ResultScreen screen;
        [SerializeField] private int count = 4000;
        [SerializeField] private float logInterval = 1f;
        [SerializeField] private float minRadius = 6f;
        [SerializeField] private float maxRadius = 70f;

        private readonly List<PendingLog> _pending = new();
        private readonly List<int> _batchIndices = new();
        private readonly List<int> _computed = new();

        private Transform[] _transforms;
        private Transform _sharedParent;
        private TransformAccessArray _access;
        private NativeArray<Orbit> _orbits;
        private NativeArray<Vector3> _positions;
        private NativeArray<int> _batchValues;
        private NativeArray<float> _batchResults;
        private JobHandle _moveHandle;
        private JobHandle _batchHandle;
        private bool _moveScheduled;
        private bool _batchScheduled;
        private float[] _nextLog;
        private int[] _lastValue;
        private float[] _lastLog;

        public static OrbitField Current { get; private set; }
        public int Count => count;
        public float LogInterval => logInterval;
        public MotionMode Motion { get; private set; } = MotionMode.Jobs;
        public OutputMode Output { get; private set; }
        public LogMode Logs { get; private set; }

        private void Awake()
        {
            Current = this;
            Spawn();
        }

        private void OnDestroy()
        {
            CompleteAll();
            if (_access.isCreated) _access.Dispose();
            if (_orbits.IsCreated) _orbits.Dispose();
            if (_positions.IsCreated) _positions.Dispose();
            if (Current == this) Current = null;
        }

        public void Configure(MotionMode motion, OutputMode output, LogMode logs)
        {
            CompleteAll();
            if (motion != Motion)
            {
                UseSharedParent(motion == MotionMode.JobsOneParent);
                Motion = motion;
            }

            if (logs != Logs) System.Array.Clear(_nextLog, 0, _nextLog.Length);
            Output = output;
            Logs = logs;
            screen.SetVisible(output == OutputMode.Screen);
        }

        public static Orbit[] GenerateOrbits(int count, float minRadius, float maxRadius)
        {
            var random = new System.Random(1709);
            var orbits = new Orbit[count];
            for (var i = 0; i < count; i++)
            {
                var radius = Mathf.Lerp(minRadius, maxRadius, Mathf.Sqrt((float)random.NextDouble()));
                var direction = random.NextDouble() < 0.5 ? -1f : 1f;
                orbits[i] = new Orbit
                {
                    Radius = radius,
                    Speed = direction * Mathf.Lerp(0.6f, 1.4f, (float)random.NextDouble()) * 12f / radius,
                    Phase = (float)random.NextDouble() * Mathf.PI * 2f,
                    Height = Mathf.Lerp(2f, 9f, (float)random.NextDouble()),
                    Spin = Mathf.Lerp(30f, 120f, (float)random.NextDouble()),
                };
            }

            return orbits;
        }

        private void Spawn()
        {
            var orbits = GenerateOrbits(count, minRadius, maxRadius);
            _transforms = new Transform[count];
            _orbits = new NativeArray<Orbit>(orbits, Allocator.Persistent);
            _positions = new NativeArray<Vector3>(count, Allocator.Persistent);
            _nextLog = new float[count];
            _lastValue = new int[count];
            _lastLog = new float[count];

            for (var i = 0; i < count; i++)
            {
                var orbiter = Instantiate(orbiterPrefab, orbits[i].PositionAt(0f), orbits[i].RotationAt(0f));
                orbiter.name = "Orbiter_" + i;
                _transforms[i] = orbiter.transform;
            }

            _access = new TransformAccessArray(_transforms);
        }

        /// <summary>IJobParallelForTransform делит работу по корневым объектам: под одним родителем всё уходит в один поток.</summary>
        private void UseSharedParent(bool shared)
        {
            if (shared && _sharedParent == null) _sharedParent = new GameObject("OrbitersRoot").transform;
            foreach (var orbiter in _transforms)
            {
                orbiter.SetParent(shared ? _sharedParent : null, true);
            }

            _access.Dispose();
            _access = new TransformAccessArray(_transforms);
        }

        private void Update()
        {
            var time = Time.time;
            if (Motion == MotionMode.MainThread)
            {
                using (MainThreadMarker.Auto())
                {
                    for (var i = 0; i < count; i++)
                    {
                        var orbit = _orbits[i];
                        var position = orbit.PositionAt(time);
                        _transforms[i].SetPositionAndRotation(position, orbit.RotationAt(time));
                        _positions[i] = position;
                    }
                }
            }
            else
            {
                using (ScheduleMoveMarker.Auto())
                {
                    _moveHandle = new OrbitJob { Orbits = _orbits, Positions = _positions, Time = time }.Schedule(_access);
                    _moveScheduled = true;
                }
            }

            ScheduleLogs(time);
        }

        private void LateUpdate()
        {
            using (CompleteMarker.Auto())
            {
                CompleteAll();
            }

            if (Output == OutputMode.None) return;
            if (Logs == LogMode.Off) OutputPositions();
            else OutputLogarithms();
        }

        private void ScheduleLogs(float time)
        {
            if (Logs == LogMode.Off) return;

            using (ScheduleLogsMarker.Auto())
            {
                if (Logs == LogMode.Batched)
                {
                    ScheduleBatch(time);
                    return;
                }

                var everyFrame = Logs == LogMode.PerObjectEveryFrame;
                for (var i = 0; i < count; i++)
                {
                    if (!everyFrame && time < _nextLog[i]) continue;
                    _nextLog[i] = time + logInterval;

                    var value = Random.Range(1, 101);
                    var result = new NativeArray<float>(1, Allocator.TempJob);
                    var handle = new LogJob { Value = value, Result = result }.Schedule();
                    _pending.Add(new PendingLog { Handle = handle, Result = result, Index = i, Value = value });
                }

                JobHandle.ScheduleBatchedJobs();
            }
        }

        private void ScheduleBatch(float time)
        {
            _batchIndices.Clear();
            for (var i = 0; i < count; i++)
            {
                if (time < _nextLog[i]) continue;
                _nextLog[i] = time + logInterval;
                _batchIndices.Add(i);
            }

            if (_batchIndices.Count == 0) return;

            _batchValues = new NativeArray<int>(_batchIndices.Count, Allocator.TempJob);
            _batchResults = new NativeArray<float>(_batchIndices.Count, Allocator.TempJob);
            for (var k = 0; k < _batchIndices.Count; k++)
            {
                var value = Random.Range(1, 101);
                _batchValues[k] = value;
                _lastValue[_batchIndices[k]] = value;
            }

            _batchHandle = new LogBatchJob { Values = _batchValues, Results = _batchResults }.Schedule(_batchIndices.Count, 64);
            _batchScheduled = true;
        }

        private void CompleteAll()
        {
            if (_moveScheduled)
            {
                _moveHandle.Complete();
                _moveScheduled = false;
            }

            _computed.Clear();
            foreach (var pending in _pending)
            {
                pending.Handle.Complete();
                _lastLog[pending.Index] = pending.Result[0];
                _lastValue[pending.Index] = pending.Value;
                pending.Result.Dispose();
                _computed.Add(pending.Index);
            }

            _pending.Clear();
            if (!_batchScheduled) return;

            _batchHandle.Complete();
            for (var k = 0; k < _batchIndices.Count; k++)
            {
                _lastLog[_batchIndices[k]] = _batchResults[k];
                _computed.Add(_batchIndices[k]);
            }

            _batchValues.Dispose();
            _batchResults.Dispose();
            _batchScheduled = false;
        }

        private void OutputPositions()
        {
            if (Output == OutputMode.Log)
            {
                using (LogOutputMarker.Auto())
                {
                    for (var i = 0; i < count; i++)
                    {
                        Debug.Log($"Объект {i}: позиция {_positions[i]}");
                    }
                }

                return;
            }

            using (ScreenOutputMarker.Auto())
            {
                screen.ShowPositions(_positions);
            }
        }

        private void OutputLogarithms()
        {
            if (Output == OutputMode.Log)
            {
                using (LogOutputMarker.Auto())
                {
                    foreach (var index in _computed)
                    {
                        Debug.Log($"Объект {index}: ln({_lastValue[index]}) = {_lastLog[index]:F3}");
                    }
                }

                return;
            }

            using (ScreenOutputMarker.Auto())
            {
                screen.ShowLogarithms(_lastValue, _lastLog, _computed.Count);
            }
        }
    }
}
