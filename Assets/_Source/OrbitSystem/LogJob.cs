using Unity.Collections;
using UnityEngine;

namespace OrbitSystem
{
    /// <summary>Часть 2: натуральный логарифм одного случайного числа от 1 до 100.</summary>
    public struct LogJob : Unity.Jobs.IJob
    {
        public int Value;
        [WriteOnly] public NativeArray<float> Result;

        public void Execute()
        {
            Result[0] = Mathf.Log(Value);
        }
    }
}
