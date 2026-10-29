using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace DuckovSharedBike
{
    // The game's loader resolves <info.ini name>.ModBehaviour.
    public sealed class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        public const string Version = "1.0.8";
        private const string LogPrefix = "[DuckovSharedBike] ";
        private bool _initialized;
        private bool _showPanel;
        private bool _inputFailed;
        private bool _panelFailed;
        private bool _snapshotFailed;
        private bool _fileLogFailed;
        private float _nextRefresh;
        private string? _logPath;
        private string _scene = "等待场景";
        private string _player = "未进入存档";
        private string _lastObservedScene = "";
        private Font? _font;
        private GUIStyle? _boxStyle;
        private GUIStyle? _titleStyle;
        private GUIStyle? _bodyStyle;
        private NativeRentalRuntime? _rentalRuntime;
        private BikeWorldController? _bikeWorld;
        private NativePatches? _patches;

        private void Awake()
        {
            // Unity calls Awake before the loader calls Setup. Load the bundled dependency before patch code is JIT compiled.
            try
            {
                var loaded=Array.Find(AppDomain.CurrentDomain.GetAssemblies(),a=>a.GetName().Name=="0Harmony");
                if (loaded==null)
                {
                    var directory=Path.GetDirectoryName(typeof(ModBehaviour).Assembly.Location);
                    if (directory!=null) System.Reflection.Assembly.LoadFrom(Path.Combine(directory,"0Harmony.dll"));
                }
            }
            catch (Exception ex) { Debug.LogWarning(LogPrefix+"Harmony preload failed: "+ex.Message); }
        }

        protected override void OnAfterSetup()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                var folder = Path.Combine(info.path, "logs");
                Directory.CreateDirectory(folder);
                _logPath = Path.Combine(folder, "bootstrap.log");
            }
            catch (Exception ex)
            {
                _fileLogFailed = true;
                Debug.LogWarning(LogPrefix + "File logging unavailable: " + ex.Message);
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            Log("Enabled version=" + Version + " unity=" + Application.unityVersion + " modPath=" + info.path);
            try { NativeContract.Validate(Log); }
            catch (Exception ex)
            {
                Log("Native contract check crashed: " + ex);
                _showPanel = true;
                RefreshSnapshot();
                return;
            }
            if (!NativeContract.Passed)
            {
                _showPanel = true;
                Log("Mod did not start: native contract failed. The game build is not the one this mod was verified against.");
                RefreshSnapshot();
                return;
            }
            _rentalRuntime = new NativeRentalRuntime(Log);
            _rentalRuntime.Enable();
            try
            {
                _bikeWorld=new BikeWorldController(_rentalRuntime,info.path,Log);
                _patches=new NativePatches();
                _patches.Enable(_bikeWorld);
                Log("Shared-bike gameplay and native patches ready.");
            }
            catch (Exception ex)
            {
                _patches?.Disable(); _patches=null;
                _bikeWorld?.Dispose(); _bikeWorld=null;
                try { _rentalRuntime.RecoverSystemRental(); _rentalRuntime.Disable(); }
                catch (Exception recovery) { Log("Activation recovery interrupted: "+recovery); }
                Log("Shared-bike activation failed: "+ex);
                _showPanel=true;
            }
            RefreshSnapshot();
        }

        protected override void OnBeforeDeactivate()
        {
            Shutdown();
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void OnApplicationQuit()=>Shutdown();

        private void Shutdown()
        {
            if (!_initialized) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _bikeWorld?.Dispose(); _bikeWorld=null;
            _rentalRuntime?.Disable();
            _patches?.Disable(); _patches=null;
            _rentalRuntime = null;
            Log("Disabled version=" + Version);
            _initialized = false;
            if (_font != null) Destroy(_font);
            _font = null;
            _boxStyle = null;
            _titleStyle = null;
            _bodyStyle = null;
        }

        private void Update()
        {
            if (!_initialized) return;
            _rentalRuntime?.Tick();
            _bikeWorld?.Tick();
            if (!_inputFailed)
            {
                try
                {
                    var keyboard = Keyboard.current;
                    if (keyboard != null)
                    {
                        var diagnostics = _bikeWorld?.DiagnosticsKey ?? Key.F9;
                        var snapshot = _bikeWorld?.SnapshotKey ?? Key.F10;
                        if (keyboard[diagnostics].wasPressedThisFrame)
                        {
                            _showPanel = !_showPanel;
                            Log("Panel visible=" + _showPanel);
                        }
                        if (keyboard[snapshot].wasPressedThisFrame)
                        {
                            RefreshSnapshot();
                            Log("Manual snapshot: " + SnapshotDescription());
                        }
                    }
                }
                catch (Exception ex)
                {
                    _inputFailed = true;
                    Log("Keyboard diagnostics disabled: " + ex);
                }
            }

            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.5f;
                RefreshSnapshot();
            }
        }

        private void LateUpdate()
        {
            if (_initialized) _bikeWorld?.LateTick();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Log("Scene loaded name=" + scene.name + " mode=" + mode);
            RefreshSnapshot();
        }

        private void RefreshSnapshot()
        {
            if (_snapshotFailed) return;
            try
            {
                var active = SceneManager.GetActiveScene();
                _scene = active.IsValid() ? active.name : "等待场景";
                var main = CharacterMainControl.Main;
                if (main == null)
                {
                    _player = "未进入存档 / 玩家尚未生成";
                }
                else
                {
                    var position = main.transform.position;
                    _player = string.Format(CultureInfo.InvariantCulture,
                        "X {0:0.0}  Y {1:0.0}  Z {2:0.0}", position.x, position.y, position.z);
                }
                if (_scene != _lastObservedScene)
                {
                    _lastObservedScene = _scene;
                    Log("Active scene changed: " + SnapshotDescription());
                }
            }
            catch (Exception ex)
            {
                _snapshotFailed = true;
                _player = "诊断读取失败，请查看日志";
                Log("Snapshot diagnostics disabled: " + ex);
            }
        }

        private string SnapshotDescription()
        {
            var scenes = new StringBuilder();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                if (i > 0) scenes.Append(", ");
                scenes.Append(SceneManager.GetSceneAt(i).name);
            }
            var main = CharacterMainControl.Main;
            return "active=" + _scene + " loaded=[" + scenes + "] playerScene=" +
                (main == null ? "none" : main.gameObject.scene.name) + " position=" + _player;
        }

        private void OnGUI()
        {
            if (!_initialized || _panelFailed) return;
            var previousMatrix = GUI.matrix;
            var previousColor = GUI.color;
            try
            {
                EnsureStyles();
                var scale = Mathf.Clamp(Screen.height / 1080f, 0.75f, 1.5f);
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
                GUI.color = Color.white;
                _bikeWorld?.Draw(_bodyStyle!,Screen.width/scale,Screen.height/scale);
                if (!_showPanel) return;
                GUI.Box(new Rect(16, 16, 500, 430), GUIContent.none, _boxStyle);
                GUI.Label(new Rect(32, 29, 464, 32), "共享单车 · 诊断 v" + Version, _titleStyle);
                var text = "状态：模组已加载\n" +
                    "原生契约：" + NativeContract.Status + "\n" +
                    "当前场景：" + _scene + "\n" +
                    "玩家位置：" + _player + "\n" +
                    (_rentalRuntime?.Summary ?? "业务系统初始化中") + "\n" +
                    (_bikeWorld?.PresentationSummary ?? "骑行姿态适配尚未启用") + "\n" +
                    "模型：" + (_bikeWorld?.VisualSummary ?? "尚未创建") + "\n" +
                    "速度：" + (_bikeWorld?.SpeedSummary ?? "-") + "\n" +
                    "存档频率：" + (_bikeWorld?.SaveIntervalSummary ?? "-") + "（上车、还车、付款、死亡立即保存）\n" +
                    (_bikeWorld==null ? "单车功能未启用，请查看日志。" : _bikeWorld.ControlsHint) + "\n" +
                    (_bikeWorld==null ? "" : _bikeWorld.DiagnosticsKeyText + " 显示/隐藏面板   " + _bikeWorld.SnapshotKeyText + " 记录当前位置\n") +
                    (_bikeWorld==null ? "F9 显示/隐藏面板\n" : "") +
                    "日志：模组目录 / logs / bootstrap.log";
                GUI.Label(new Rect(32, 68, 464, 320), text, _bodyStyle);
                if (_bikeWorld != null && GUI.Button(new Rect(32, 392, 240, 30), "脱困：重新启用附近通道"))
                {
                    var result = _bikeWorld.RepairNearbyTravelPoints();
                    Log("Manual travel repair: " + result);
                    _bikeWorld.Notify(result, true);
                }
                if (_bikeWorld != null) GUI.Label(new Rect(284, 392, 212, 30), "仅用于卡住时", _bodyStyle);
            }
            catch (Exception ex)
            {
                _panelFailed = true;
                Log("Diagnostic panel disabled: " + ex);
            }
            finally
            {
                GUI.matrix = previousMatrix;
                GUI.color = previousColor;
            }
        }

        private void EnsureStyles()
        {
            if (_bodyStyle != null) return;
            try
            {
                _font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 16);
            }
            catch (Exception ex)
            {
                Log("Using default GUI font: " + ex.Message);
            }
            _boxStyle = new GUIStyle(GUI.skin.box);
            _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 21, fontStyle = FontStyle.Bold };
            _titleStyle.normal.textColor = new Color(0.35f, 0.94f, 0.73f);
            _bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
            _bodyStyle.normal.textColor = Color.white;
            if (_font != null)
            {
                _titleStyle.font = _font;
                _bodyStyle.font = _font;
            }
        }

        private void Log(string message)
        {
            Debug.Log(LogPrefix + message);
            if (_fileLogFailed || _logPath == null) return;
            try
            {
                var line = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine;
                File.AppendAllText(_logPath, line, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                _fileLogFailed = true;
                Debug.LogWarning(LogPrefix + "File logging disabled: " + ex.Message);
            }
        }
    }
}
