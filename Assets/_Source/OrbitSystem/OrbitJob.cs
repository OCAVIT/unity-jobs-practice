using Unity.Collections;
using UnityEngine;
using UnityEngine.Jobs;

namespace OrbitSystem
{
    /// <summary>Часть 1: двигает объекты по кругу на рабочих потоках и сохраняет позиции для вывода.</summary>
    public struct OrbitJob : IJobParallelForTransform
    {
        [ReadOnly] public NativeArray<Orbit> Orbits;
        [WriteOnly] public NativeArray<Vector3> Positions;
        public float Time;

        public void Execute(int index, TransformAccess transform)
        {
            var orbit = Orbits[index];
            var position = orbit.PositionAt(Time);
            transform.position = position;
            transform.rotation = orbit.RotationAt(Time);
            Positions[index] = position;
        }
    }
}
