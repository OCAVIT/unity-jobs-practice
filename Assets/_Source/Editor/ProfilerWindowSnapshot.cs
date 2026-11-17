using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace ProfilerCaptureSystem
{
    /// <summary>Открывает худший кадр сценария в окне Profiler, выделяет джоб или виновника и сохраняет снимок окна.</summary>
    public static class ProfilerWindowSnapshot
    {
        private static readonly Rect WindowRect = new(30f, 40f, 1720f, 960f);
        private const string JobGroupKey = "Profiler.Timeline.GroupExpanded.Job";
        private const string SavedPrefKey = "ProfilerLab.JobGroupPref";

        /// <summary>Разворачивает группу потоков Job в Timeline, иначе рабочие потоки на снимках не видны.</summary>
        public static void ExpandJobGroup()
        {
            SessionState.SetInt(SavedPrefKey, EditorPrefs.HasKey(JobGroupKey) ? (EditorPrefs.GetBool(JobGroupKey) ? 1 : 0) : -1);
            SessionState.SetBool(JobGroupKey, true);
            EditorPrefs.SetBool(JobGroupKey, true);
        }

        public static void RestorePreferences()
        {
            var saved = SessionState.GetInt(SavedPrefKey, -1);
            if (saved < 0) EditorPrefs.DeleteKey(JobGroupKey);
            else EditorPrefs.SetBool(JobGroupKey, saved == 1);
        }

        public static ProfilerWindow Open()
        {
            var window = EditorWindow.GetWindow<ProfilerWindow>();
            window.position = WindowRect;
            return window;
        }

        public static void Select(ScenarioResult result)
        {
            if (result.frames == 0) return;

            var window = Open();
            var frame = !string.IsNullOrEmpty(result.focus) && result.focusFrame >= 0 ? result.focusFrame : result.worstFrame;
            window.selectedFrameIndex = frame;
            try
            {
                if (!SelectFocus(window, result, frame)) SelectCulprit(window, result, frame);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Snapshot] Не удалось выделить маркер: " + exception.Message);
            }

            window.Repaint();
        }

        public static void Save(string path)
        {
            var window = Open();
            var width = Mathf.RoundToInt(window.position.width * EditorGUIUtility.pixelsPerPoint);
            var height = Mathf.RoundToInt(window.position.height * EditorGUIUtility.pixelsPerPoint);
            var texture = TryGrab(window, width, height) ?? ReadScreen(window, width, height);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        /// <summary>Самый долгий сэмпл джоба на рабочих потоках: на снимке видно, что работа ушла с главного потока.</summary>
        private static bool SelectFocus(ProfilerWindow window, ScenarioResult result, int frame)
        {
            if (string.IsNullOrEmpty(result.focus)) return false;

            var bestThread = -1;
            var bestSample = -1;
            var longest = -1f;
            for (var thread = 1; thread < 256; thread++)
            {
                using var raw = ProfilerDriver.GetRawFrameDataView(frame, thread);
                if (!raw.valid) break;
                for (var sample = 0; sample < raw.sampleCount; sample++)
                {
                    var sampleName = raw.GetSampleName(sample);
                    if (sampleName == null || !sampleName.Contains(result.focus)) continue;
                    var time = raw.GetSampleTimeMs(sample);
                    if (time <= longest) continue;
                    longest = time;
                    bestThread = thread;
                    bestSample = sample;
                }
            }

            if (bestThread < 0) return false;
            using var best = ProfilerDriver.GetRawFrameDataView(frame, bestThread);
            return Apply(window, best, bestSample);
        }

        private static void SelectCulprit(ProfilerWindow window, ScenarioResult result, int frame)
        {
            if (string.IsNullOrEmpty(result.culprit)) return;

            using var raw = ProfilerDriver.GetRawFrameDataView(frame, 0);
            var best = -1;
            var longest = -1f;
            for (var sample = 0; sample < raw.sampleCount; sample++)
            {
                if (raw.GetSampleName(sample) != result.culprit) continue;
                var time = raw.GetSampleTimeMs(sample);
                if (time <= longest) continue;
                longest = time;
                best = sample;
            }

            if (best >= 0) Apply(window, raw, best);
        }

        private static bool Apply(ProfilerWindow window, RawFrameDataView raw, int sample)
        {
            var controller = window.GetFrameTimeViewSampleSelectionController(ProfilerWindow.cpuModuleIdentifier);
            return controller.SetSelection(new ProfilerTimeSampleSelection(raw.frameIndex, raw.threadGroupName, raw.threadName, raw.threadId, sample, raw.GetSampleName(sample)));
        }

        /// <summary>Копирует содержимое окна напрямую, поэтому другие окна поверх на снимок не попадают.</summary>
        private static Texture2D TryGrab(EditorWindow window, int width, int height)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var parent = typeof(EditorWindow).GetField("m_Parent", Flags)?.GetValue(window);
            var grab = parent?.GetType().GetMethods(Flags).FirstOrDefault(method => method.Name == "GrabPixels" && method.GetParameters().Length == 2);
            if (grab == null) return null;

            var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                grab.Invoke(parent, new object[] { target, new Rect(0f, 0f, width, height) });
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                RenderTexture.active = previous;
                FlipVertically(texture, width, height);
                return texture;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Snapshot] GrabPixels: " + exception.Message);
                return null;
            }
            finally
            {
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        /// <summary>GrabPixels отдаёт строки снизу вверх.</summary>
        private static void FlipVertically(Texture2D texture, int width, int height)
        {
            var source = texture.GetPixels32();
            var flipped = new Color32[source.Length];
            for (var y = 0; y < height; y++)
            {
                Array.Copy(source, y * width, flipped, (height - 1 - y) * width, width);
            }

            texture.SetPixels32(flipped);
            texture.Apply();
        }

        private static Texture2D ReadScreen(EditorWindow window, int width, int height)
        {
            var scale = EditorGUIUtility.pixelsPerPoint;
            var colors = InternalEditorUtility.ReadScreenPixel(new Vector2(window.position.x * scale, window.position.y * scale), width, height);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.SetPixels(colors);
            texture.Apply();
            return texture;
        }
    }
}
