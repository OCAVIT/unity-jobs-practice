using System.IO;
using OrbitSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProfilerCaptureSystem
{
    /// <summary>Снимок сцены для README: те же орбиты, что и в игре, рендер основной камеры без запуска Play Mode.</summary>
    public static class PlacePreview
    {
        private const int Width = 1600;
        private const int Height = 900;
        private const float Moment = 2f;

        [MenuItem("Profiler Lab/Снимок сцены для README")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene(PlaceBuilder.ScenePath);
            var field = Object.FindAnyObjectByType<OrbitField>();
            var settings = new SerializedObject(field);
            var prefab = (GameObject)settings.FindProperty("orbiterPrefab").objectReferenceValue;
            var orbits = OrbitField.GenerateOrbits(settings.FindProperty("count").intValue, settings.FindProperty("minRadius").floatValue, settings.FindProperty("maxRadius").floatValue);

            var preview = new GameObject("PreviewOrbiters");
            foreach (var orbit in orbits)
            {
                Object.Instantiate(prefab, orbit.PositionAt(Moment), orbit.RotationAt(Moment), preview.transform);
            }

            var camera = Camera.main;
            Render(camera, "place.png");

            var close = System.Array.FindIndex(orbits, orbit => orbit.Radius > 18f && orbit.Radius < 22f);
            var focus = orbits[close].PositionAt(Moment);
            var original = camera.transform.position;
            var originalRotation = camera.transform.rotation;
            var originalFov = camera.fieldOfView;
            camera.transform.position = focus + new Vector3(-2.4f, 1.1f, -2.8f);
            camera.transform.LookAt(focus + new Vector3(0.4f, 0f, 0f));
            camera.fieldOfView = 45f;
            Render(camera, "orbiter.png");
            camera.transform.SetPositionAndRotation(original, originalRotation);
            camera.fieldOfView = originalFov;

            Object.DestroyImmediate(preview);
            Debug.Log($"[PlacePreview] Снимки сцены готовы, объектов {orbits.Length}");
        }

        private static void Render(Camera camera, string fileName)
        {
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;

            var path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "docs", "screenshots", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.DestroyImmediate(image);
            target.Release();
            Object.DestroyImmediate(target);
        }

        /// <summary>Точка входа для командной строки: сцена, билд и снимок за один запуск.</summary>
        public static void BuildAll()
        {
            PlaceBuilder.BuildEverything();
            Capture();
        }
    }
}
