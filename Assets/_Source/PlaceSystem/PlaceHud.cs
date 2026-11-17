using System.Text;
using OrbitSystem;
using UnityEngine;
using UnityEngine.UI;

namespace PlaceSystem
{
    /// <summary>Подсказка и счётчик FPS. Подсказка перестраивается только при смене режима.</summary>
    public sealed class PlaceHud : MonoBehaviour
    {
        private static readonly string[] MotionNames = { "главный поток (Update)", "IJobParallelForTransform", "Jobs, один общий родитель" };
        private static readonly string[] OutputNames = { "нет", "в лог", "на экран" };
        private static readonly string[] LogNames = { "выключены", "IJob раз в N с", "IJob каждый кадр", "один IJobParallelFor" };

        [SerializeField] private Text legend;
        [SerializeField] private Text fps;

        private readonly StringBuilder _builder = new(512);
        private int _shownState = -1;
        private float _windowStart;
        private int _frames;

        private void Update()
        {
            var field = OrbitField.Current;
            if (field != null)
            {
                var state = (int)field.Motion * 100 + (int)field.Output * 10 + (int)field.Logs;
                if (state != _shownState) Rebuild(field, state);
            }

            _frames++;
            var elapsed = Time.unscaledTime - _windowStart;
            if (elapsed < 0.5f) return;
            fps.text = Mathf.RoundToInt(_frames / elapsed) + " FPS";
            _frames = 0;
            _windowStart = Time.unscaledTime;
        }

        private void Rebuild(OrbitField field, int state)
        {
            _shownState = state;
            _builder.Clear();
            _builder.Append("Объектов: ").Append(field.Count).Append('\n');
            _builder.Append("Движение: ").Append(MotionNames[(int)field.Motion]).Append("  [1, 2, 3]\n");
            _builder.Append("Вывод результатов: ").Append(OutputNames[(int)field.Output]).Append("  [Q]\n");
            _builder.Append("Логарифмы: ").Append(LogNames[(int)field.Logs]).Append(", N = ").Append(field.LogInterval).Append(" с  [W]");
            legend.text = _builder.ToString();
        }
    }
}
