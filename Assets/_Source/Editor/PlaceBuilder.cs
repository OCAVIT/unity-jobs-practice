using System;
using System.IO;
using OrbitSystem;
using PlaceSystem;
using ScenarioSystem;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProfilerCaptureSystem
{
    /// <summary>Собирает сцену кодом: меш и материалы, префаб, камеру, свет, интерфейс и настройки билда.</summary>
    public static class PlaceBuilder
    {
        public const string ScenePath = Art + "/Scenes/OrbitPlace.unity";
        public const string PlayerPath = "Build/Windows/JobsPractice.exe";

        private const string Art = "Assets/_Presentation";
        private const string Materials = Art + "/Materials";
        private const string Models = Art + "/Models";
        private const string Prefabs = Art + "/Prefabs";

        private static Font _font;

        [MenuItem("Profiler Lab/1. Собрать сцену")]
        public static void BuildPlace()
        {
            foreach (var folder in new[] { Materials, Models, Prefabs, Art + "/Scenes" })
            {
                Directory.CreateDirectory(folder);
            }

            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var body = CreateMaterial("OrbiterBody", new Color(0.55f, 0.63f, 0.78f));
            body.SetFloat("_Metallic", 0.85f);
            body.SetFloat("_Glossiness", 0.72f);
            var core = CreateMaterial("OrbiterCore", new Color(1f, 0.55f, 0.15f));
            core.EnableKeyword("_EMISSION");
            core.SetColor("_EmissionColor", new Color(1f, 0.45f, 0.1f) * 2.2f);
            core.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            var floor = CreateMaterial("Floor", new Color(0.1f, 0.11f, 0.15f));
            EditorUtility.SetDirty(body);
            EditorUtility.SetDirty(core);

            var mesh = OrbiterMesh.Build();
            AssetDatabase.CreateAsset(mesh, Models + "/Orbiter.asset");
            var orbiter = CreateOrbiterPrefab(mesh, body, core);

            CreateWorld(floor);
            CreateCanvas(out var legend, out var fps, out var resultPanel, out var resultText);

            var fieldObject = new GameObject("OrbitField");
            var screen = fieldObject.AddComponent<ResultScreen>();
            Wire(screen, ("panel", resultPanel), ("text", resultText));
            Wire(fieldObject.AddComponent<OrbitField>(), ("orbiterPrefab", orbiter), ("screen", screen));

            var place = new GameObject("Place");
            place.AddComponent<PlaceBootstrap>();
            place.AddComponent<PlaceInput>();
            Wire(place.AddComponent<PlaceHud>(), ("legend", legend), ("fps", fps));
            new GameObject("ScenarioRunner", typeof(ScenarioRunner));

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            ApplyPlayerSettings();
            AssetDatabase.SaveAssets();
            Debug.Log($"[PlaceBuilder] Сцена собрана, треугольников в меше: {mesh.triangles.Length / 3}");
        }

        [MenuItem("Profiler Lab/2. Собрать PC-билд (Development)")]
        public static void BuildPlayer()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PlayerPath));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = PlayerPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            });

            Debug.Log($"[PlaceBuilder] Билд: {report.summary.result}, ошибок {report.summary.totalErrors}");
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Билд не собрался");
        }

        /// <summary>Точка входа для запуска из командной строки.</summary>
        public static void BuildEverything()
        {
            BuildPlace();
            BuildPlayer();
        }

        private static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "KTSHKI";
            PlayerSettings.productName = "JobsPractice";
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        }

        private static GameObject CreateOrbiterPrefab(Mesh mesh, Material body, Material core)
        {
            var orbiter = new GameObject("Orbiter", typeof(MeshFilter), typeof(MeshRenderer));
            orbiter.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = orbiter.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { body, core };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var prefab = PrefabUtility.SaveAsPrefabAsset(orbiter, Prefabs + "/Orbiter.prefab");
            Object.DestroyImmediate(orbiter);
            return prefab;
        }

        private static void CreateWorld(Material floorMaterial)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(18f, 1f, 18f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            Object.DestroyImmediate(floor.GetComponent<Collider>());

            var sun = new GameObject("Sun", typeof(Light));
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = sun.GetComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            light.intensity = 1.2f;

            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
            cameraObject.transform.position = new Vector3(0f, 62f, -98f);
            cameraObject.transform.rotation = Quaternion.Euler(32f, 0f, 0f);
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.06f, 0.1f);
            camera.farClipPlane = 400f;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.52f);
        }

        private static void CreateCanvas(out Text legend, out Text fps, out GameObject resultPanel, out Text resultText)
        {
            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);

            var legendPanel = CreatePanel("LegendPanel", canvasObject.transform, new Color(0f, 0f, 0f, 0.55f));
            Place(legendPanel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -12f), new Vector2(470f, 104f));
            legend = CreateText("Legend", legendPanel, 16);
            Stretch(legend.rectTransform, 10f);

            fps = CreateText("Fps", canvasObject.transform, 26);
            fps.alignment = TextAnchor.UpperCenter;
            Place(fps.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(240f, 40f));

            var panel = CreatePanel("ResultPanel", canvasObject.transform, new Color(0f, 0f, 0f, 0.7f));
            Place(panel, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(330f, 0f));
            resultText = CreateText("Results", panel, 13);
            Stretch(resultText.rectTransform, 10f);
            resultPanel = panel.gameObject;
            resultPanel.SetActive(false);
        }

        private static RectTransform CreatePanel(string name, Transform parent, Color color)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = color;
            return panel.GetComponent<RectTransform>();
        }

        private static Text CreateText(string name, Transform parent, int size)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static Material CreateMaterial(string name, Color color)
        {
            var material = new Material(Shader.Find("Standard")) { color = color, enableInstancing = true };
            AssetDatabase.CreateAsset(material, $"{Materials}/{name}.mat");
            return material;
        }

        private static void Wire(Object target, params (string field, Object value)[] references)
        {
            var serialized = new SerializedObject(target);
            foreach (var (field, value) in references)
            {
                var property = serialized.FindProperty(field) ?? throw new ArgumentException($"{target.GetType().Name} не содержит поля {field}");
                property.objectReferenceValue = value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
