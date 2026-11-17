using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ScenarioSystem;
using UnityEditor;
using UnityEditor.Networking.PlayerConnection;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Networking.PlayerConnection;
using Debug = UnityEngine.Debug;

namespace ProfilerCaptureSystem
{
    /// <summary>
    /// Проводит все замеры: Play Mode, Deep Profile и PC-билд с подключением Profiler из редактора.
    /// Состояние лежит в SessionState, потому что вход в Play Mode перезагружает домен.
    /// </summary>
    [InitializeOnLoad]
    public static class CaptureSession
    {
        private enum Stage { EditorEnter, EditorRun, EditorLeave, DeepArm, DeepEnter, DeepRun, DeepLeave, DeepDisarm, PlayerLaunch, PlayerConnect, PlayerRun, PlayerQuit, Done }

        private enum Phase { Start, Running, Flush, Snap }

        private interface ICaptureTarget
        {
            bool Ready { get; }
            void Start(string id, float duration);
            bool IsFinished(string id);
        }

        private sealed class PlayModeTarget : ICaptureTarget
        {
            public bool Ready => EditorApplication.isPlaying && ScenarioRunner.Instance != null && !ScenarioRunner.Instance.IsBusy;
            public void Start(string id, float duration) => ScenarioRunner.Instance.Run(id, duration);
            public bool IsFinished(string id) => ScenarioRunner.Instance != null && ScenarioRunner.Instance.LastFinished == id;
        }

        private sealed class PlayerTarget : ICaptureTarget
        {
            private static string _lastDone;

            public PlayerTarget()
            {
                EditorConnection.instance.Initialize();
                EditorConnection.instance.Register(ScenarioLink.ReplyId, OnReply);
            }

            public bool Ready => IsAttachedToPlayer();

            public void Start(string id, float duration)
            {
                _lastDone = null;
                EditorConnection.instance.Send(ScenarioLink.CommandId, ScenarioLink.Encode(ScenarioLink.RunPrefix + id));
            }

            public bool IsFinished(string id) => _lastDone == id;

            public void Quit() => EditorConnection.instance.Send(ScenarioLink.CommandId, ScenarioLink.Encode(ScenarioLink.Quit));

            // EditorConnection принимает только статические методы или методы наследников UnityEngine.Object.
            private static void OnReply(MessageEventArgs message)
            {
                var text = ScenarioLink.Decode(message.data);
                if (text.StartsWith(ScenarioLink.DonePrefix)) _lastDone = text.Substring(ScenarioLink.DonePrefix.Length);
            }
        }

        private const string KeyActive = "ProfilerLab.Active";
        private const string KeyStage = "ProfilerLab.Stage";
        private const string KeyIndex = "ProfilerLab.Index";
        private const string KeyStarted = "ProfilerLab.Started";
        private const string KeyPlan = "ProfilerLab.Plan";
        private const string KeyExit = "ProfilerLab.Exit";

        private const int PlanEditor = 1;
        private const int PlanDeep = 2;
        private const int PlanPlayer = 4;

        private const float TimeLimit = 1800f;
        private const float DeepDuration = 1.5f;
        private const double SettleSeconds = 2.5;
        private const double FlushSeconds = 0.4;
        private const double SnapSeconds = 0.8;
        private const double CooldownSeconds = 2.2;
        private const double ConnectTimeout = 60;

        private static readonly PlayModeTarget PlayMode = new();

        private static Phase _phase;
        private static double _waitUntil;
        private static double _readySince;
        private static double _deadline;
        private static double _connectStarted;
        private static bool _connectRequested;
        private static ScenarioResult _result;
        private static PlayerTarget _player;

        private static double Now => EditorApplication.timeSinceStartup;
        private static string Root => Path.GetDirectoryName(Application.dataPath);
        private static string PlayerLog => Path.Combine(Root, "Captures", "player.log");
        private static string ProgressFile => Path.Combine(Root, "Captures", "progress.txt");

        private static Stage CurrentStage
        {
            get => (Stage)SessionState.GetInt(KeyStage, 0);
            set => SessionState.SetInt(KeyStage, (int)value);
        }

        static CaptureSession()
        {
            if (SessionState.GetBool(KeyActive, false)) EditorApplication.update += Tick;
        }

        [MenuItem("Profiler Lab/3. Записать все замеры")]
        public static void RunFromMenu() => Begin(PlanEditor | PlanPlayer, false);

        public static void RunAll() => Begin(PlanEditor | PlanPlayer, true);
        public static void RunEditorOnly() => Begin(PlanEditor, true);
        public static void RunDeepOnly() => Begin(PlanDeep, true);
        public static void RunPlayerOnly() => Begin(PlanPlayer, true);

        private static void Begin(int plan, bool exitWhenDone)
        {
            Directory.CreateDirectory(Path.Combine(Root, "Captures"));
            File.WriteAllText(ProgressFile, string.Empty);

            SessionState.SetBool(KeyActive, true);
            SessionState.SetInt(KeyPlan, plan);
            SessionState.SetBool(KeyExit, exitWhenDone);
            SessionState.SetFloat(KeyStarted, (float)Now);

            ProfilerDriver.enabled = false;
            ProfilerDriver.profileEditor = false;
            ProfilerDriver.deepProfiling = false;
            ProfilerWindowSnapshot.ExpandJobGroup();
            ProfilerWindowSnapshot.Open();
            EditorSceneManager.OpenScene(PlaceBuilder.ScenePath);

            Log("Запись начата");
            StartBlockAfter(-1);
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            try
            {
                if (Now - SessionState.GetFloat(KeyStarted, 0f) > TimeLimit) throw new TimeoutException("Превышено общее время записи");
                Advance();
            }
            catch (Exception exception)
            {
                Log("ОШИБКА: " + exception);
                Finish(1);
            }
        }

        private static void Advance()
        {
            switch (CurrentStage)
            {
                case Stage.EditorEnter:
                    EnterPlay(Stage.EditorRun);
                    break;

                case Stage.EditorRun:
                    if (RunScenarios(EditorScenarios(), PlayMode, "editor", 0f, "Editor (Play Mode)")) LeavePlay(Stage.EditorLeave);
                    break;

                case Stage.EditorLeave:
                    if (!EditorApplication.isPlayingOrWillChangePlaymode) StartBlockAfter(0);
                    break;

                case Stage.DeepArm:
                    ProfilerDriver.deepProfiling = true;
                    EditorUtility.RequestScriptReload();
                    _waitUntil = Now + 3;
                    Go(Stage.DeepEnter);
                    break;

                case Stage.DeepEnter:
                    if (Now >= _waitUntil) EnterPlay(Stage.DeepRun);
                    break;

                case Stage.DeepRun:
                    var deep = ScenarioCatalog.All.Where(scenario => scenario.DeepOnly).ToList();
                    if (RunScenarios(deep, PlayMode, "editor-deep", DeepDuration, "Editor (Play Mode), Deep Profile")) LeavePlay(Stage.DeepLeave);
                    break;

                case Stage.DeepLeave:
                    if (EditorApplication.isPlayingOrWillChangePlaymode) break;
                    ProfilerDriver.deepProfiling = false;
                    EditorUtility.RequestScriptReload();
                    _waitUntil = Now + 3;
                    Go(Stage.DeepDisarm);
                    break;

                case Stage.DeepDisarm:
                    if (Now >= _waitUntil) StartBlockAfter(1);
                    break;

                case Stage.PlayerLaunch:
                    LaunchPlayer();
                    break;

                case Stage.PlayerConnect:
                    ConnectToPlayer();
                    break;

                case Stage.PlayerRun:
                    _player ??= new PlayerTarget();
                    if (RunScenarios(ScenarioCatalog.All.Where(scenario => !scenario.DeepOnly).ToList(), _player, "player", 0f, Connection()))
                    {
                        _player.Quit();
                        _waitUntil = Now + 4;
                        Go(Stage.PlayerQuit);
                    }

                    break;

                case Stage.PlayerQuit:
                    if (Now < _waitUntil) break;
                    StartBlockAfter(2);
                    break;

                case Stage.Done:
                    Log("Запись завершена");
                    Finish(0);
                    break;
            }
        }

        private static IReadOnlyList<Scenario> EditorScenarios()
        {
            return ScenarioCatalog.All.Where(scenario => !scenario.PlayerOnly && !scenario.DeepOnly).ToList();
        }

        private static void StartBlockAfter(int block)
        {
            var plan = SessionState.GetInt(KeyPlan, 0);
            var next = Stage.Done;
            if (block < 0 && (plan & PlanEditor) != 0) next = Stage.EditorEnter;
            else if (block < 1 && (plan & PlanDeep) != 0) next = Stage.DeepArm;
            else if (block < 2 && (plan & PlanPlayer) != 0) next = Stage.PlayerLaunch;
            Go(next);
        }

        private static void Go(Stage stage)
        {
            CurrentStage = stage;
            SessionState.SetInt(KeyIndex, 0);
            _phase = Phase.Start;
            _readySince = 0;
            Log("Этап: " + stage);
        }

        private static void EnterPlay(Stage next)
        {
            EditorApplication.isPlaying = true;
            Go(next);
        }

        private static void LeavePlay(Stage next)
        {
            ProfilerDriver.enabled = false;
            EditorApplication.isPlaying = false;
            Go(next);
        }

        /// <summary>Идёт по списку сценариев, по шагу за вызов. Возвращает истину, когда список пройден.</summary>
        private static bool RunScenarios(IReadOnlyList<Scenario> list, ICaptureTarget target, string label, float duration, string connection)
        {
            var index = SessionState.GetInt(KeyIndex, 0);
            if (index >= list.Count) return true;

            var scenario = list[index];
            switch (_phase)
            {
                case Phase.Start:
                    if (Now < _waitUntil || !target.Ready)
                    {
                        _readySince = 0;
                        break;
                    }

                    if (_readySince == 0) _readySince = Now;
                    if (Now < _readySince + SettleSeconds) break;

                    ProfilerDriver.ClearAllFrames();
                    ProfilerDriver.enabled = true;
                    target.Start(scenario.Id, duration);
                    _deadline = Now + scenario.Duration + 120;
                    _phase = Phase.Running;
                    break;

                case Phase.Running:
                    var timedOut = Now > _deadline;
                    if (timedOut) Log($"{label}/{scenario.Id}: ответа от цели нет, берём то, что записано");
                    if (timedOut || target.IsFinished(scenario.Id))
                    {
                        _waitUntil = Now + FlushSeconds;
                        _phase = Phase.Flush;
                    }

                    break;

                case Phase.Flush:
                    if (Now < _waitUntil) break;
                    ProfilerDriver.enabled = false;
                    if (EditorApplication.isPlaying) EditorApplication.isPaused = true;
                    Directory.CreateDirectory(Path.Combine(Root, "Captures", label));
                    ProfilerDriver.SaveProfile(Path.Combine(Root, "Captures", label, scenario.Id + ".data"));

                    _result = FrameAnalyzer.Analyze(scenario, label, connection);
                    var dataFolder = Path.Combine(Root, "docs", "data", label);
                    Directory.CreateDirectory(dataFolder);
                    File.WriteAllText(Path.Combine(dataFolder, scenario.Id + ".json"), JsonUtility.ToJson(_result, true));
                    Log($"{label}/{scenario.Id}: кадров {_result.frames}, FPS {_result.avgFps:F1}, мин {_result.minFps:F1}, худший кадр CPU {_result.worstCpuMs:F1} мс, причина {_result.culprit}");

                    ProfilerWindowSnapshot.Select(_result);
                    _waitUntil = Now + SnapSeconds;
                    _phase = Phase.Snap;
                    break;

                case Phase.Snap:
                    if (Now < _waitUntil) break;
                    ProfilerWindowSnapshot.Save(Path.Combine(Root, "docs", "screenshots", label, scenario.Id + ".png"));
                    if (EditorApplication.isPlaying) EditorApplication.isPaused = false;
                    SessionState.SetInt(KeyIndex, index + 1);
                    _waitUntil = Now + CooldownSeconds;
                    _readySince = 0;
                    _phase = Phase.Start;
                    break;
            }

            return false;
        }

        private static void LaunchPlayer()
        {
            if (FindListedPlayer() != 0)
            {
                Log("Билд уже запущен, подключаюсь к нему");
                BeginConnecting();
                return;
            }

            var executable = Path.Combine(Root, PlaceBuilder.PlayerPath);
            if (!File.Exists(executable)) throw new FileNotFoundException("Нет PC-билда, сначала соберите его", executable);
            if (File.Exists(PlayerLog)) File.Delete(PlayerLog);

            var arguments = $"-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile \"{PlayerLog}\"";
            Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable) });
            BeginConnecting();
        }

        private static void BeginConnecting()
        {
            _connectStarted = Now;
            _connectRequested = false;
            _waitUntil = Now + 2;
            Go(Stage.PlayerConnect);
        }

        private static int FindListedPlayer()
        {
            return ProfilerDriver.GetAvailableProfilers().FirstOrDefault(id => ProfilerDriver.GetConnectionIdentifier(id).Contains("WindowsPlayer"));
        }

        private static void ConnectToPlayer()
        {
            if (Now < _waitUntil) return;
            if (Now - _connectStarted > ConnectTimeout) throw new TimeoutException("Profiler не подключился к билду");

            if (IsAttachedToPlayer())
            {
                Log("Profiler подключён: " + Connection());
                _player = new PlayerTarget();
                _waitUntil = Now + 3;
                Go(Stage.PlayerRun);
                return;
            }

            var listed = FindListedPlayer();
            var port = ReadPlayerPort();
            if (listed != 0)
            {
                Log("Выбираю билд в списке целей Profiler: " + ProfilerDriver.GetConnectionIdentifier(listed));
                ProfilerDriver.connectedProfiler = listed;
                _connectRequested = true;
            }
            else if (port != null)
            {
                Log("Подключаюсь к билду на порт " + port);
                ProfilerDriver.DirectURLConnect("127.0.0.1:" + port);
                _connectRequested = true;
            }

            _waitUntil = Now + (_connectRequested ? 3 : 1);
        }

        private static string ReadPlayerPort()
        {
            if (!File.Exists(PlayerLog)) return null;
            using var stream = new FileStream(PlayerLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var match = Regex.Match(reader.ReadToEnd(), @"\[Port\] (\d{4,5})");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static bool IsAttachedToPlayer()
        {
            var connection = Connection();
            return connection.Length > 0 && !connection.Contains("Editor") && !connection.Contains("Play Mode");
        }

        private static string Connection()
        {
            return ProfilerDriver.GetConnectionIdentifier(ProfilerDriver.connectedProfiler);
        }

        private static void Finish(int exitCode)
        {
            EditorApplication.update -= Tick;
            SessionState.SetBool(KeyActive, false);
            ProfilerDriver.enabled = false;
            ProfilerDriver.deepProfiling = false;
            ProfilerWindowSnapshot.RestorePreferences();

            if (SessionState.GetBool(KeyExit, false)) EditorApplication.Exit(exitCode);
        }

        private static void Log(string message)
        {
            Debug.Log("[Capture] " + message);
            File.AppendAllText(ProgressFile, DateTime.Now.ToString("HH:mm:ss ") + message + Environment.NewLine);
        }
    }
}
