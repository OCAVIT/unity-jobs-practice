using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace OrbitSystem
{
    /// <summary>Для сравнения: те же логарифмы для всех объектов одним параллельным джобом.</summary>
    public struct LogBatchJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> Values;
        [WriteOnly] public NativeArray<float> Results;

        public void Execute(int index)
        {
            Results[index] = Mathf.Log(Values[index]);
        }
    }
}
