using System.Collections.Generic;
using OrbitSystem;

namespace ScenarioSystem
{
    /// <summary>Режимы, которые бот включает по очереди. Те же режимы доступны с клавиатуры.</summary>
    public static class ScenarioCatalog
    {
        public static readonly IReadOnlyList<Scenario> All = new[]
        {
            Mode("p1-main", "Часть 1: движение в Update на главном потоке, без Jobs", MotionMode.MainThread, OutputMode.None, LogMode.Off, null),
            Mode("p1-jobs", "Часть 1: движение через IJobParallelForTransform", MotionMode.Jobs, OutputMode.None, LogMode.Off, "OrbitJob"),
            Mode("p1-jobs-log", "Часть 1: Jobs и вывод позиций в лог, Debug.Log на каждый объект", MotionMode.Jobs, OutputMode.Log, LogMode.Off, "OrbitJob"),
            Mode("p1-jobs-screen", "Часть 1: Jobs и вывод позиций на экран", MotionMode.Jobs, OutputMode.Screen, LogMode.Off, "OrbitJob"),
            Mode("p1-one-parent", "Часть 1: Jobs, но все объекты под одним родителем", MotionMode.JobsOneParent, OutputMode.None, LogMode.Off, "OrbitJob"),
            Mode("p2-jobs", "Часть 2: IJob с логарифмом раз в N = 1 с для каждого объекта", MotionMode.Jobs, OutputMode.None, LogMode.PerObject, "LogJob"),
            Mode("p2-jobs-log", "Часть 2: IJob и вывод логарифмов в лог", MotionMode.Jobs, OutputMode.Log, LogMode.PerObject, "LogJob"),
            Mode("p2-jobs-screen", "Часть 2: IJob и вывод логарифмов на экран", MotionMode.Jobs, OutputMode.Screen, LogMode.PerObject, "LogJob"),
            Mode("p2-every-frame", "Часть 2: IJob для каждого объекта каждый кадр", MotionMode.Jobs, OutputMode.None, LogMode.PerObjectEveryFrame, "LogJob"),
            Mode("p2-batched", "Часть 2: те же логарифмы одним IJobParallelFor раз в 1 с", MotionMode.Jobs, OutputMode.None, LogMode.Batched, "LogBatchJob"),
        };

        private static Scenario Mode(string id, string title, MotionMode motion, OutputMode output, LogMode logs, string focus)
        {
            return new Scenario(id, title, context => context.Field.Configure(motion, output, logs), focus: focus);
        }
    }
}
