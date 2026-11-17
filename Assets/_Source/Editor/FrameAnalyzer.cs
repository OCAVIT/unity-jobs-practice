using System;
using System.Collections.Generic;
using System.Linq;
using ScenarioSystem;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace ProfilerCaptureSystem
{
    /// <summary>Разбирает записанные Profiler кадры: время, GC, счётчики и путь до самого дорогого маркера.</summary>
    public static class FrameAnalyzer
    {
        private const string Wait = "WaitForTargetFPS";
        private const string Loop = "PlayerLoop";
        private const string Alloc = "GC.Alloc";
        private const string Collect = "GC.Collect";
        private const string RenderThread = "Render Thread";
        private const string JobGroup = "Job";
        private const string JobWait = "WaitForJobGroupID";
        private const string OwnMarkerPrefix = "Orbit.";
        private const float PathShare = 0.3f;
        private const float Megabyte = 1024f * 1024f;

        private static readonly HashSet<string> RenderIdle = new() { "Gfx.WaitForCommands", "Idle", "Semaphore.WaitForSignal", "Gfx.PresentFrame" };

        public static ScenarioResult Analyze(Scenario scenario, string target, string connection)
        {
            var result = new ScenarioResult { scenario = scenario.Id, title = scenario.Title, target = target, connection = connection };
            var frames = ScenarioFrames();
            // В первом и последнем кадре сценария переключается режим, это не установившаяся работа.
            if (frames.Count > 4)
            {
                frames.RemoveAt(frames.Count - 1);
                frames.RemoveAt(0);
            }

            if (frames.Count == 0) return result;

            var self = new Dictionary<string, SampleStat>();
            var owners = new Dictionary<string, SampleStat>();
            var render = new Dictionary<string, SampleStat>();
            var jobs = new Dictionary<string, SampleStat>();
            var focusThreads = new HashSet<string>();
            float focusMs = 0f, jobWork = 0f, jobWait = 0f, focusThreadSum = 0f, focusSpan = 0f, orbitMain = 0f, orbitMainMax = 0f;
            var focusFrames = 0;
            var focusBest = 0f;
            var frameTimes = new List<float>(frames.Count);
            var cpuTimes = new List<float>(frames.Count);
            float loopTotal = 0f, gcBytes = 0f, batches = 0f, setPass = 0f, triangles = 0f;
            var worst = -1;

            foreach (var frame in frames)
            {
                using var view = Open(frame, 0);
                var top = new List<int>();
                view.GetItemChildren(view.GetRootItemID(), top);

                var frameWait = 0f;
                var frameOrbit = 0f;
                var frameLoop = 0f;
                foreach (var item in top)
                {
                    if (view.GetItemName(item) != Loop) continue;
                    frameLoop = Total(view, item);
                    loopTotal += frameLoop;
                    Walk(view, item, (id, parent) =>
                    {
                        var name = view.GetItemName(id);
                        if (name == JobWait) jobWait += view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
                        if (name.StartsWith(OwnMarkerPrefix)) frameOrbit += Total(view, id);
                        if (name == Wait)
                        {
                            frameWait += Total(view, id);
                            return false;
                        }

                        Add(self, name, view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime), Calls(view, id), 0f);
                        if (name == Alloc)
                        {
                            var bytes = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnGcMemory);
                            gcBytes += bytes;
                            Add(owners, parent < 0 ? "?" : view.GetItemName(parent), 0f, Calls(view, id), bytes / 1024f);
                        }
                        else if (name == Collect)
                        {
                            result.gcCollects += Calls(view, id);
                            result.gcCollectMs += Total(view, id);
                        }

                        return true;
                    });
                }

                var frameMs = view.frameTimeMs;
                var cpu = frameLoop - frameWait;
                frameTimes.Add(frameMs);
                cpuTimes.Add(cpu);
                if (cpu > result.worstCpuMs)
                {
                    result.worstCpuMs = cpu;
                    worst = frame;
                }

                ReadCounters(result, frame, ref batches, ref setPass, ref triangles);
                ReadRenderThread(frame, render);
                orbitMain += frameOrbit;
                orbitMainMax = Mathf.Max(orbitMainMax, frameOrbit);
                var focusBefore = focusMs;
                ReadJobThreads(frame, scenario.Focus, jobs, focusThreads, ref focusMs, ref jobWork, ref focusThreadSum, ref focusSpan, ref focusFrames);
                if (focusMs - focusBefore > focusBest)
                {
                    focusBest = focusMs - focusBefore;
                    result.focusFrame = frame;
                }
            }

            FillTimes(result, frameTimes, cpuTimes, loopTotal, gcBytes);
            result.avgBatches = batches / frames.Count;
            result.avgSetPass = setPass / frames.Count;
            result.avgTrianglesK = triangles / frames.Count / 1000f;
            ReadStartMemory(result, frames[0]);

            result.topSelf = Top(self, stat => stat.ms, 10);
            result.topGcOwners = Top(owners, stat => stat.kb, 6);
            result.renderThreadSelf = Top(render, stat => stat.ms, 6);
            result.jobThreadSelf = Top(jobs, stat => stat.ms, 6);
            result.focus = scenario.Focus;
            result.focusThreads = focusThreads.Count;
            result.focusFrames = focusFrames;
            result.focusThreadsPerFrame = focusFrames > 0 ? focusThreadSum / focusFrames : 0f;
            result.focusMsPerFrame = focusFrames > 0 ? focusMs / focusFrames : 0f;
            result.focusSpanMs = focusFrames > 0 ? focusSpan / focusFrames : 0f;
            result.orbitMainMs = orbitMain / frames.Count;
            result.orbitMainMaxMs = orbitMainMax;
            result.jobWorkMsPerFrame = jobWork / frames.Count;
            result.mainWaitForJobsMs = jobWait / frames.Count;
            InspectWorstFrame(result, worst);
            return result;
        }

        private static void FillTimes(ScenarioResult result, List<float> frameTimes, List<float> cpuTimes, float loopTotal, float gcBytes)
        {
            var count = frameTimes.Count;
            var totalMs = frameTimes.Sum();
            var sorted = frameTimes.OrderBy(value => value).ToList();
            result.frames = count;
            result.avgFrameMs = totalMs / count;
            result.avgFps = 1000f / result.avgFrameMs;
            result.p99FrameMs = sorted[Mathf.Clamp(Mathf.CeilToInt(count * 0.99f) - 1, 0, count - 1)];
            result.maxFrameMs = sorted[^1];
            result.low1Fps = 1000f / result.p99FrameMs;
            result.minFps = 1000f / result.maxFrameMs;
            result.avgCpuMs = cpuTimes.Sum() / count;
            result.maxCpuMs = cpuTimes.Max();
            result.avgPlayerLoopMs = loopTotal / count;
            result.avgOutsideLoopMs = (totalMs - loopTotal) / count;
            result.framesOver16 = frameTimes.Count(value => value > 18f);
            result.framesOver33 = frameTimes.Count(value => value > 33.33f);
            result.framesOver100 = frameTimes.Count(value => value > 100f);
            result.gcKbPerFrame = gcBytes / 1024f / count;
            result.gcTotalMb = gcBytes / Megabyte;
        }

        private static void InspectWorstFrame(ScenarioResult result, int frame)
        {
            if (frame < 0) return;
            result.worstFrame = frame;

            using var view = Open(frame, 0);
            var top = new List<int>();
            view.GetItemChildren(view.GetRootItemID(), top);
            var loop = top.FirstOrDefault(item => view.GetItemName(item) == Loop);
            if (loop == 0) return;

            var self = new Dictionary<string, SampleStat>();
            Walk(view, loop, (id, _) =>
            {
                var name = view.GetItemName(id);
                if (name == Wait) return false;
                Add(self, name, view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime), Calls(view, id), 0f);
                return true;
            });
            result.worstSelf = Top(self, stat => stat.ms, 8);

            var current = loop;
            result.worstPath.Add(Stat(view, current));
            var children = new List<int>();
            while (true)
            {
                children.Clear();
                view.GetItemChildren(current, children);
                var candidates = children.Where(child => view.GetItemName(child) != Wait).ToList();
                if (candidates.Count == 0) break;

                var best = candidates.OrderByDescending(child => Total(view, child)).First();
                if (Total(view, best) < Total(view, current) * PathShare) break;
                result.worstPath.Add(Stat(view, best));
                current = best;
            }

            result.culprit = result.worstPath[^1].name;
        }

        private static SampleStat Stat(HierarchyFrameDataView view, int id)
        {
            return new SampleStat { name = view.GetItemName(id), ms = Total(view, id), calls = Calls(view, id), kb = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnGcMemory) / 1024f };
        }

        private static List<int> ScenarioFrames()
        {
            var frames = new List<int>();
            for (var frame = ProfilerDriver.firstFrameIndex; frame >= 0 && frame <= ProfilerDriver.lastFrameIndex; frame++)
            {
                using var raw = ProfilerDriver.GetRawFrameDataView(frame, 0);
                if (!raw.valid) continue;
                var marker = raw.GetMarkerId(ScenarioRunner.FrameMarkerName);
                if (marker == FrameDataView.invalidMarkerId) continue;
                for (var sample = 0; sample < raw.sampleCount; sample++)
                {
                    if (raw.GetSampleMarkerId(sample) != marker) continue;
                    frames.Add(frame);
                    break;
                }
            }

            return frames;
        }

        private static void ReadCounters(ScenarioResult result, int frame, ref float batches, ref float setPass, ref float triangles)
        {
            using var raw = ProfilerDriver.GetRawFrameDataView(frame, 0);
            result.usedPeakMb = Mathf.Max(result.usedPeakMb, Counter(raw, "Total Used Memory") / Megabyte);
            result.texturePeakMb = Mathf.Max(result.texturePeakMb, Counter(raw, "Texture Memory") / Megabyte);
            result.objectsPeak = Math.Max(result.objectsPeak, Counter(raw, "Object Count"));
            batches += Math.Max(0, Counter(raw, "Batches Count"));
            setPass += Math.Max(0, Counter(raw, "SetPass Calls Count"));
            triangles += Math.Max(0, Counter(raw, "Triangles Count"));
        }

        private static void ReadStartMemory(ScenarioResult result, int firstFrame)
        {
            using var raw = ProfilerDriver.GetRawFrameDataView(firstFrame, 0);
            result.usedStartMb = Counter(raw, "Total Used Memory") / Megabyte;
            result.textureStartMb = Counter(raw, "Texture Memory") / Megabyte;
            result.objectsStart = Counter(raw, "Object Count");
        }

        private static long Counter(RawFrameDataView raw, string name)
        {
            var marker = raw.GetMarkerId(name);
            if (marker == FrameDataView.invalidMarkerId || !raw.HasCounterValue(marker)) return -1;
            return raw.GetCounterValueAsLong(marker);
        }

        private static readonly HashSet<string> JobIdle = new() { "Idle", "Semaphore.WaitForSignal", "WaitForSignal" };

        /// <summary>
        /// Работа на рабочих потоках Job System. Для джоба сценария считается по каждому кадру:
        /// на скольких потоках он шёл, сколько времени занял суммарно и сколько по часам от первого до последнего сэмпла.
        /// </summary>
        private static void ReadJobThreads(int frame, string focus, Dictionary<string, SampleStat> jobs, HashSet<string> focusThreads, ref float focusMs, ref float jobWork, ref float focusThreadSum, ref float focusSpan, ref int focusFrames)
        {
            var threadsWithFocus = 0;
            var first = ulong.MaxValue;
            var last = 0UL;
            for (var thread = 1; thread < 256; thread++)
            {
                using var raw = ProfilerDriver.GetRawFrameDataView(frame, thread);
                if (!raw.valid) break;
                if (raw.threadGroupName != JobGroup) continue;

                if (!string.IsNullOrEmpty(focus))
                {
                    var found = false;
                    for (var sample = 0; sample < raw.sampleCount; sample++)
                    {
                        var sampleName = raw.GetSampleName(sample);
                        if (sampleName == null || !sampleName.Contains(focus)) continue;
                        var start = raw.GetSampleStartTimeNs(sample);
                        var duration = raw.GetSampleTimeNs(sample);
                        focusMs += duration / 1000000f;
                        first = Math.Min(first, start);
                        last = Math.Max(last, start + duration);
                        found = true;
                    }

                    if (found)
                    {
                        threadsWithFocus++;
                        focusThreads.Add(raw.threadName);
                    }
                }

                var work = 0f;
                using var view = Open(frame, thread);
                Walk(view, view.GetRootItemID(), (id, _) =>
                {
                    var name = view.GetItemName(id);
                    if (string.IsNullOrEmpty(name)) return true;
                    if (JobIdle.Contains(name)) return false;
                    var self = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
                    Add(jobs, name, self, Calls(view, id), 0f);
                    work += self;
                    return true;
                });
                jobWork += work;
            }

            if (threadsWithFocus == 0) return;
            focusFrames++;
            focusThreadSum += threadsWithFocus;
            focusSpan += (last - first) / 1000000f;
        }

        private static void ReadRenderThread(int frame, Dictionary<string, SampleStat> render)
        {
            for (var thread = 1; thread < 256; thread++)
            {
                using var raw = ProfilerDriver.GetRawFrameDataView(frame, thread);
                if (!raw.valid) return;
                if (raw.threadName != RenderThread) continue;

                using var view = Open(frame, thread);
                Walk(view, view.GetRootItemID(), (id, _) =>
                {
                    var name = view.GetItemName(id);
                    if (string.IsNullOrEmpty(name)) return true;
                    if (RenderIdle.Contains(name)) return false;
                    Add(render, name, view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime), Calls(view, id), 0f);
                    return true;
                });
                return;
            }
        }

        private static HierarchyFrameDataView Open(int frame, int thread)
        {
            return ProfilerDriver.GetHierarchyFrameDataView(frame, thread, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false);
        }

        private delegate bool Visit(int id, int parent);

        /// <summary>Обход дерева без рекурсии. Visit возвращает false, если дочерние элементы не нужны.</summary>
        private static void Walk(HierarchyFrameDataView view, int start, Visit visit)
        {
            var stack = new Stack<(int id, int parent)>();
            var children = new List<int>();
            stack.Push((start, -1));
            while (stack.Count > 0)
            {
                var (id, parent) = stack.Pop();
                if (!visit(id, parent)) continue;
                children.Clear();
                view.GetItemChildren(id, children);
                foreach (var child in children)
                {
                    stack.Push((child, id));
                }
            }
        }

        private static float Total(HierarchyFrameDataView view, int id)
        {
            return view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnTotalTime);
        }

        private static int Calls(HierarchyFrameDataView view, int id)
        {
            return (int)view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnCalls);
        }

        private static void Add(Dictionary<string, SampleStat> table, string name, float ms, int calls, float kb)
        {
            if (!table.TryGetValue(name, out var stat))
            {
                stat = new SampleStat { name = name };
                table.Add(name, stat);
            }

            stat.ms += ms;
            stat.calls += calls;
            stat.kb += kb;
        }

        private static List<SampleStat> Top(Dictionary<string, SampleStat> table, System.Func<SampleStat, float> key, int count)
        {
            return table.Values.OrderByDescending(key).Take(count).ToList();
        }
    }
}
