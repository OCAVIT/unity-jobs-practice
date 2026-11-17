using OrbitSystem;
using UnityEngine;

namespace PlaceSystem
{
    /// <summary>Ручное управление: 1, 2, 3 выбирают способ движения, Q меняет вывод, W меняет режим логарифмов.</summary>
    public sealed class PlaceInput : MonoBehaviour
    {
        private void Update()
        {
            var field = OrbitField.Current;
            if (field == null) return;

            var motion = field.Motion;
            if (Input.GetKeyDown(KeyCode.Alpha1)) motion = MotionMode.MainThread;
            if (Input.GetKeyDown(KeyCode.Alpha2)) motion = MotionMode.Jobs;
            if (Input.GetKeyDown(KeyCode.Alpha3)) motion = MotionMode.JobsOneParent;

            var output = field.Output;
            if (Input.GetKeyDown(KeyCode.Q)) output = (OutputMode)(((int)output + 1) % 3);

            var logs = field.Logs;
            if (Input.GetKeyDown(KeyCode.W)) logs = (LogMode)(((int)logs + 1) % 4);

            if (motion != field.Motion || output != field.Output || logs != field.Logs) field.Configure(motion, output, logs);
        }
    }
}
