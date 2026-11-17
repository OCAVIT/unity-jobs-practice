using UnityEngine;

namespace OrbitSystem
{
    /// <summary>Параметры круговой орбиты одного объекта. Одна и та же формула для главного потока и для джоба.</summary>
    public struct Orbit
    {
        public float Radius;
        public float Speed;
        public float Phase;
        public float Height;
        public float Spin;

        public Vector3 PositionAt(float time)
        {
            var angle = Phase + Speed * time;
            var bob = Mathf.Sin(time * 1.5f + Phase) * 0.6f;
            return new Vector3(Mathf.Cos(angle) * Radius, Height + bob, Mathf.Sin(angle) * Radius);
        }

        public Quaternion RotationAt(float time)
        {
            var angle = Phase + Speed * time;
            return Quaternion.Euler(20f, -angle * Mathf.Rad2Deg, time * Spin);
        }
    }
}
