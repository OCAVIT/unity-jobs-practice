using OrbitSystem;

namespace ScenarioSystem
{
    public readonly struct ScenarioContext
    {
        public OrbitField Field { get; }
        public float Elapsed { get; }
        public int Frame { get; }

        public ScenarioContext(OrbitField field, float elapsed, int frame)
        {
            Field = field;
            Elapsed = elapsed;
            Frame = frame;
        }
    }
}
