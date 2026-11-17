using System;
using System.Collections.Generic;

namespace ProfilerCaptureSystem
{
    [Serializable]
    public sealed class SampleStat
    {
        public string name;
        public float ms;
        public int calls;
        public float kb;
    }

    /// <summary>Итог одного сценария: всё считано из кадров, записанных Profiler.</summary>
    [Serializable]
    public sealed class ScenarioResult
    {
        public string scenario;
        public string title;
        public string target;
        public string connection;
        public int frames;

        public float avgFps;
        public float low1Fps;
        public float minFps;
        public float avgFrameMs;
        public float p99FrameMs;
        public float maxFrameMs;
        public float avgCpuMs;
        public float maxCpuMs;
        public float avgPlayerLoopMs;
        public float avgOutsideLoopMs;
        public int framesOver16;
        public int framesOver33;
        public int framesOver100;

        public float gcKbPerFrame;
        public float gcTotalMb;
        public int gcCollects;
        public float gcCollectMs;

        public float avgBatches;
        public float avgSetPass;
        public float avgTrianglesK;
        public float usedStartMb;
        public float usedPeakMb;
        public float textureStartMb;
        public float texturePeakMb;
        public long objectsStart;
        public long objectsPeak;

        public int worstFrame;
        public float worstCpuMs;
        public string culprit;

        public List<SampleStat> topSelf = new();
        public List<SampleStat> topGcOwners = new();
        public List<SampleStat> worstSelf = new();
        public List<SampleStat> worstPath = new();
        public List<SampleStat> renderThreadSelf = new();

        public string focus;
        public int focusThreads;
        public int focusFrames;
        public int focusFrame = -1;
        public float focusThreadsPerFrame;
        public float focusMsPerFrame;
        public float focusSpanMs;
        public float orbitMainMs;
        public float orbitMainMaxMs;
        public float jobWorkMsPerFrame;
        public float mainWaitForJobsMs;
        public List<SampleStat> jobThreadSelf = new();
    }
}
