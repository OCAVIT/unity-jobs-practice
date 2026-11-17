using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace OrbitSystem
{
    /// <summary>Вывод результатов вычислений на игровой экран: первые строки и сводка по всем объектам.</summary>
    public sealed class ResultScreen : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text text;
        [SerializeField] private int lines = 40;

        private readonly StringBuilder _builder = new(4096);

        public void SetVisible(bool visible)
        {
            panel.SetActive(visible);
        }

        public void ShowPositions(NativeArray<Vector3> positions)
        {
            var radius = 0f;
            for (var i = 0; i < positions.Length; i++)
            {
                radius += new Vector2(positions[i].x, positions[i].z).magnitude;
            }

            _builder.Clear();
            _builder.Append("ПОЗИЦИИ, объектов: ").Append(positions.Length).Append('\n');
            _builder.Append("Средний радиус: ").Append((radius / positions.Length).ToString("F2")).Append(" м\n\n");
            for (var i = 0; i < lines && i < positions.Length; i++)
            {
                var position = positions[i];
                _builder.Append('#').Append(i).Append(": ")
                    .Append(position.x.ToString("F2")).Append("; ")
                    .Append(position.y.ToString("F2")).Append("; ")
                    .Append(position.z.ToString("F2")).Append('\n');
            }

            text.text = _builder.ToString();
        }

        public void ShowLogarithms(int[] values, float[] logarithms, int computedThisFrame)
        {
            _builder.Clear();
            _builder.Append("ЛОГАРИФМЫ, посчитано в этом кадре: ").Append(computedThisFrame).Append("\n\n");
            for (var i = 0; i < lines && i < values.Length; i++)
            {
                _builder.Append('#').Append(i).Append(": ln(").Append(values[i]).Append(") = ")
                    .Append(logarithms[i].ToString("F3")).Append('\n');
            }

            text.text = _builder.ToString();
        }
    }
}
