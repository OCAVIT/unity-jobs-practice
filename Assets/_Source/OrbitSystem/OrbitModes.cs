namespace OrbitSystem
{
    public enum MotionMode
    {
        MainThread,
        Jobs,
        JobsOneParent,
    }

    public enum OutputMode
    {
        None,
        Log,
        Screen,
    }

    public enum LogMode
    {
        Off,
        PerObject,
        PerObjectEveryFrame,
        Batched,
    }
}
