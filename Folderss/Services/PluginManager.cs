using Folderss.Controls;
using Folderss.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows;

namespace Folderss.Services
{
    /// <summary>
    /// 플러그인 로드와 본체 기능 제공. 플러그인은 ⋯ 메뉴에서 처음 선택할 때(설정 탭이 있으면 설정 창을 열 때) 로드한다.
    /// 로드·초기화·화면 생성 실패는 모두 잡아서 알리고 본체는 계속 동작한다.
    /// 로드한 플러그인은 프로세스가 끝날 때까지 남는다 — WPF 형식을 쓰는 어셈블리는 사실상 언로드할 수 없어서,
    /// 제거·교체는 다시 시작한 뒤에 반영된다.
    /// </summary>
    public static class PluginManager
    {
        private static readonly Dictionary<string, LoadedPlugin> Loaded =
            new Dictionary<string, LoadedPlugin>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<Assembly> ReportedAssemblies = new HashSet<Assembly>();
        private static readonly object SessionGate = new object();
        private static PluginSessionRecord _session;
        private static bool _userRequestedExit;
        private static bool _sessionEnding;
        private static bool _orderlyExit;

        private static string AppDataDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Folderss"); }
        }

        public static string PluginsDirectory { get { return Path.Combine(AppDataDirectory, "plugins"); } }
        private static string ExtractRoot { get { return Path.Combine(PluginsDirectory, "extracted"); } }
        private static string DataRoot { get { return Path.Combine(AppDataDirectory, "plugin-data"); } }
        private static string SessionDirectory { get { return Path.Combine(AppDataDirectory, "plugin-sessions"); } }
        public static string LogPath { get { return Path.Combine(AppDataDirectory, "plugin-log.txt"); } }

        /// <summary>플러그인 폴더 패널에서 파일을 열 때 호출된다 (메인 창이 뷰어 탭으로 연다).</summary>
        public static Action<string> OpenFileHandler { get; set; }

        public static List<PluginManifest> ListInstalled(List<string> errors = null)
        {
            return PluginPackage.List(PluginsDirectory, errors);
        }

        public static bool IsLoaded(string id)
        {
            return Loaded.ContainsKey(id);
        }

        /// <summary>이번 실행에서 이미 로드한 플러그인. 없으면 null (로드하지 않는다).</summary>
        public static LoadedPlugin GetLoaded(string id)
        {
            LoadedPlugin loaded;
            return Loaded.TryGetValue(id, out loaded) ? loaded : null;
        }

        /// <summary>플러그인을 로드해 초기화한다. 이미 로드했으면 그대로 돌려준다. 실패하면 null과 원인.</summary>
        public static LoadedPlugin TryLoad(PluginManifest manifest, out string error)
        {
            error = null;
            LoadedPlugin loaded;
            if (Loaded.TryGetValue(manifest.Id, out loaded))
                return loaded;

            try
            {
                var directory = PluginPackage.Extract(manifest, ExtractRoot);
                var assemblyPath = Path.Combine(directory, manifest.Assembly);
                var context = new PluginLoadContext(manifest.Id, assemblyPath);
                var assembly = context.LoadFromAssemblyPath(assemblyPath);
                var type = assembly.GetType(manifest.Type, true);
                var plugin = Activator.CreateInstance(type) as IFolderssPlugin;
                if (plugin == null)
                    throw new InvalidOperationException(manifest.Type + "이 IFolderssPlugin을 구현하지 않습니다. " +
                        "Folderss.PluginContract를 다른 버전으로 참조했을 수 있습니다.");

                var host = new PluginHost(manifest, directory, Path.Combine(DataRoot, manifest.Id));
                plugin.Initialize(host);
                loaded = new LoadedPlugin(manifest, plugin, host);
                Loaded[manifest.Id] = loaded;
                RecordLoaded(manifest.Id);
                return loaded;
            }
            catch (Exception ex)
            {
                error = Describe(ex);
                return null;
            }
        }

        // ── 종료 감지 ────────────────────────────────────────────────────────
        // 같은 프로세스 안의 플러그인이 본체를 끝내는 것(Application.Shutdown, Environment.Exit, 프로세스 Kill, FailFast,
        // 백그라운드 스레드의 처리되지 않은 예외)은 막을 수 없다. 대신 알 수 있는 만큼 원인을 plugin-log.txt에 남기고,
        // 기록할 틈도 없는 강제 종료는 plugin-sessions 기록이 남는 것으로 다음 시작 때 알린다. 플러그인을 로드한 실행만 대상이다.

        /// <summary>App 시작 시 한 번. 종료 경로 감시를 건다.</summary>
        public static void InstallExitHooks(Application app)
        {
            app.SessionEnding += (sender, args) => _sessionEnding = true;
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var exception = args.ExceptionObject as Exception;
                var owner = exception != null ? FindPluginId(exception) : null;
                RecordExit(string.Format("처리되지 않은 예외로 종료 ({0}): {1}",
                    owner != null ? "플러그인 " + owner : "본체 또는 알 수 없음",
                    exception != null ? exception.ToString() : Convert.ToString(args.ExceptionObject)));
            };
            AppDomain.CurrentDomain.ProcessExit += (sender, args) =>
            {
                // 정상 종료는 Application.Exit(OnApplicationExit)를 먼저 거친다. 그 없이 여기 왔으면 Environment.Exit 같은 즉시 종료다.
                if (_orderlyExit)
                    return;
                var owner = FindPluginId(new StackTrace());
                RecordExit("WPF 종료 절차 없이 프로세스가 끝남(Environment.Exit 등). 호출한 플러그인: " + (owner ?? "스택에서 찾지 못함"));
            };
        }

        /// <summary>사용자가 ⋯ 메뉴 > 종료 등으로 직접 끝낼 때 메인 창이 부른다.</summary>
        public static void MarkUserRequestedExit()
        {
            _userRequestedExit = true;
        }

        /// <summary>Application.Exit에서 부른다. 사용자·OS가 끝낸 게 아니면 코드(Application.Shutdown)가 끝낸 것이다.</summary>
        public static void OnApplicationExit()
        {
            _orderlyExit = true;
            if (_userRequestedExit || _sessionEnding)
            {
                lock (SessionGate)
                {
                    if (_session == null) return;
                    try { PluginSessionLog.Delete(SessionDirectory, _session.ProcessId); } catch (Exception) { }
                    _session = null;
                }
                return;
            }
            // Application.Shutdown은 호출 후 나중에 처리되어 이 시점 스택에는 호출자가 없다 — 로드된 플러그인을 용의자로만 남긴다.
            RecordExit("사용자 종료가 아닌 코드에서 Application.Shutdown이 호출됨. 호출자는 알 수 없음(로드된 플러그인 목록 참고)");
        }

        /// <summary>이전 실행 중 플러그인을 로드한 채 정상 종료되지 않은 기록이 있으면 로그에 남기고 알린다.</summary>
        public static void ReportPreviousAbnormalExits()
        {
            List<PluginSessionRecord> stale;
            try
            {
                stale = PluginSessionLog.TakeStale(SessionDirectory, IsSameProcessRunning);
            }
            catch (Exception)
            {
                return;
            }
            if (stale.Count == 0)
                return;

            var text = string.Join("\n\n", stale.Select(PluginSessionLog.Describe));
            TryAppendLog("이전 실행이 정상 종료되지 않음\n" + text);
            MessageBox.Show("플러그인을 로드한 이전 실행이 정상적으로 종료되지 않았습니다.\n\n" + text + "\n\n로그: " + LogPath,
                "플러그인", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static bool IsSameProcessRunning(PluginSessionRecord record)
        {
            try
            {
                using (var process = Process.GetProcessById(record.ProcessId))
                    return Math.Abs((process.StartTime.ToUniversalTime() - record.StartedAtUtc).TotalSeconds) < 2;
            }
            catch (Exception)
            {
                return false; // 없는 pid(ArgumentException) 또는 접근 불가 — 같은 사용자의 Folderss라면 접근 가능하다
            }
        }

        private static void RecordLoaded(string id)
        {
            lock (SessionGate)
            {
                if (_session == null)
                {
                    using (var current = Process.GetCurrentProcess())
                        _session = new PluginSessionRecord { ProcessId = current.Id, StartedAtUtc = current.StartTime.ToUniversalTime() };
                }
                _session.Plugins.Add(id);
                TryWriteSession();
            }
        }

        private static void RecordExit(string reason)
        {
            lock (SessionGate)
            {
                if (_session == null)
                    return; // 플러그인을 로드하지 않은 실행
                // 처음 알아낸 원인을 남긴다(예: 처리되지 않은 예외 뒤에 종료 처리가 이어져도 예외 쪽이 더 구체적이다).
                if (_session.ExitReason == null)
                    _session.ExitReason = reason;
                TryWriteSession();
                TryAppendLog(reason + "\n로드된 플러그인: " + string.Join(", ", _session.Plugins));
            }
        }

        // 진단 기록 실패는 플러그인 실행이나 종료를 막을 이유가 아니라서 넘어간다(종료 중에는 알릴 방법도 없다).
        private static void TryWriteSession()
        {
            try { PluginSessionLog.Write(SessionDirectory, _session); } catch (Exception) { }
        }

        private static void TryAppendLog(string message)
        {
            try { PluginSessionLog.AppendLog(LogPath, message); } catch (Exception) { }
        }

        /// <summary>플러그인 팝업 창을 연다. 실패하면 메시지만 보이고 끝낸다.</summary>
        public static void ShowPluginWindow(PluginManifest manifest, Window owner)
        {
            string error;
            var loaded = TryLoad(manifest, out error);
            FrameworkElement view = null;
            if (loaded != null)
            {
                try
                {
                    view = loaded.Plugin.CreateView();
                    if (view == null)
                        error = "CreateView()가 null을 반환했습니다.";
                }
                catch (Exception ex)
                {
                    error = Describe(ex);
                }
            }

            if (view == null)
            {
                ShowError(manifest.DisplayName, "플러그인을 열지 못했습니다.", error);
                return;
            }

            try
            {
                var window = new Window
                {
                    Title = manifest.DisplayName,
                    Width = 900,
                    Height = 600,
                    MinWidth = 320,
                    MinHeight = 240,
                    Owner = owner,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ResizeMode = ResizeMode.CanResizeWithGrip,
                    Content = view
                };
                window.SetResourceReference(Window.BackgroundProperty, "WindowBackground");
                window.SetResourceReference(Window.ForegroundProperty, "PrimaryText");
                window.SetResourceReference(Window.FontFamilyProperty, "AppFontFamily");
                window.Show();
            }
            catch (Exception ex)
            {
                ShowError(manifest.DisplayName, "플러그인 창을 표시하지 못했습니다.", Describe(ex));
            }
        }

        /// <summary>
        /// 처리되지 않은 UI 스레드 예외가 플러그인 코드에서 났는지. App이 이 경우만 처리하고 넘어간다
        /// (본체 예외는 기존대로 둔다). 같은 플러그인의 오류는 한 번만 알린다 — 레이아웃 중 예외는 반복되기 때문.
        /// </summary>
        public static bool TryHandleUnhandled(Exception exception)
        {
            var assembly = FindPluginAssembly(exception);
            if (assembly == null)
                return false;

            TryAppendLog("플러그인 " + DescribeOwner(assembly) + "의 처리되지 않은 UI 예외(본체는 계속 실행): " + exception);
            if (ReportedAssemblies.Add(assembly))
            {
                ShowError(DescribeOwner(assembly),
                    "플러그인에서 오류가 발생했습니다. 이 플러그인의 이후 오류는 표시하지 않습니다. 문제가 계속되면 플러그인을 제거하세요.",
                    Describe(exception));
            }
            return true;
        }

        private static Assembly FindPluginAssembly(Exception exception)
        {
            for (var ex = exception; ex != null; ex = ex.InnerException)
            {
                var assembly = FindPluginAssembly(new StackTrace(ex));
                if (assembly != null)
                    return assembly;
            }
            return null;
        }

        private static Assembly FindPluginAssembly(StackTrace trace)
        {
            foreach (var frame in trace.GetFrames())
            {
                var assembly = frame.GetMethod()?.DeclaringType?.Assembly;
                if (assembly != null && AssemblyLoadContext.GetLoadContext(assembly) is PluginLoadContext)
                    return assembly;
            }
            return null;
        }

        private static string FindPluginId(Exception exception)
        {
            return DescribeOwner(FindPluginAssembly(exception));
        }

        private static string FindPluginId(StackTrace trace)
        {
            return DescribeOwner(FindPluginAssembly(trace));
        }

        private static string DescribeOwner(Assembly assembly)
        {
            if (assembly == null)
                return null;
            var context = AssemblyLoadContext.GetLoadContext(assembly) as PluginLoadContext;
            return context?.PluginId ?? assembly.GetName().Name;
        }

        private static string Describe(Exception ex)
        {
            var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
            return inner.GetType().Name + ": " + inner.Message;
        }

        private static void ShowError(string pluginName, string message, string detail)
        {
            MessageBox.Show(message + "\n\n" + detail, "플러그인 - " + pluginName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>
        /// 플러그인 전용 로드 영역. 계약 DLL은 반드시 본체(기본 영역)의 것을 쓴다 — 플러그인 폴더의 사본을 올리면
        /// 같은 이름이라도 다른 형식이 되어 IFolderssPlugin 형변환이 실패한다. 나머지 의존성은 플러그인 폴더에서 찾는다.
        /// </summary>
        private sealed class PluginLoadContext : AssemblyLoadContext
        {
            private static readonly string ContractName = typeof(IFolderssPlugin).Assembly.GetName().Name;
            private readonly AssemblyDependencyResolver _resolver;

            public PluginLoadContext(string pluginId, string assemblyPath) : base("plugin:" + pluginId)
            {
                PluginId = pluginId;
                _resolver = new AssemblyDependencyResolver(assemblyPath);
            }

            public string PluginId { get; private set; }

            protected override Assembly Load(AssemblyName assemblyName)
            {
                if (string.Equals(assemblyName.Name, ContractName, StringComparison.OrdinalIgnoreCase))
                    return null;
                var path = _resolver.ResolveAssemblyToPath(assemblyName);
                return path != null ? LoadFromAssemblyPath(path) : null;
            }

            protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
            {
                var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
                return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
            }
        }
    }

    public sealed class LoadedPlugin
    {
        public LoadedPlugin(PluginManifest manifest, IFolderssPlugin plugin, PluginHost host)
        {
            Manifest = manifest;
            Plugin = plugin;
            Host = host;
        }

        public PluginManifest Manifest { get; private set; }
        public IFolderssPlugin Plugin { get; private set; }
        public PluginHost Host { get; private set; }
    }

    /// <summary>플러그인 하나에 주는 <see cref="IPluginManager"/>.</summary>
    public sealed class PluginHost : IPluginManager
    {
        private readonly string _dataDirectory;
        private readonly PluginSettingsStore _settings;
        private readonly List<IPluginSettingsPage> _settingsPages = new List<IPluginSettingsPage>();

        public PluginHost(PluginManifest manifest, string pluginDirectory, string dataDirectory)
        {
            PluginId = manifest.Id;
            PluginDirectory = pluginDirectory;
            _dataDirectory = dataDirectory;
            _settings = new PluginSettingsStore(Path.Combine(dataDirectory, "settings.json"));
        }

        public string PluginId { get; private set; }
        public string PluginDirectory { get; private set; }

        public string DataDirectory
        {
            get
            {
                Directory.CreateDirectory(_dataDirectory);
                return _dataDirectory;
            }
        }

        public IReadOnlyList<IPluginSettingsPage> SettingsPages { get { return _settingsPages; } }

        public string GetSetting(string key) { return _settings.Get(key); }
        public void SetSetting(string key, string value) { _settings.Set(key, value); }
        public IReadOnlyDictionary<string, string> GetAllSettings() { return _settings.GetAll(); }

        public IReadOnlyDictionary<string, string> GetAppSettings()
        {
            return PluginAppSettings.Build(ThemeManager.CurrentTheme.ToString(), GitSettingsService.Load(),
                DiffSettingsService.Load(), ConsoleSettingsService.Load());
        }

        public IFolderPanel CreateFolderPanel(string path)
        {
            return new PluginFolderPanel(path);
        }

        public void AddSettingsPage(IPluginSettingsPage page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            _settingsPages.Add(page);
        }
    }

    /// <summary>
    /// 플러그인 팝업용 폴더 패널. 메인 창의 활성 패널 추적(Activated/PathChanged)에는 붙이지 않는다 —
    /// 붙이면 ⋯ 메뉴 명령이 팝업 속 패널을 대상으로 삼고, 팝업을 닫은 뒤에도 그 패널을 가리키게 된다.
    /// 파일 열기만 메인 창 뷰어 탭으로 보낸다.
    /// </summary>
    internal sealed class PluginFolderPanel : IFolderPanel
    {
        private readonly FolderBrowser _browser = new FolderBrowser();

        public PluginFolderPanel(string path)
        {
            _browser.FileOpenRequested += (sender, file) =>
            {
                var open = PluginManager.OpenFileHandler;
                if (open != null) open(file);
            };
            _browser.PathChanged += (sender, args) => PathChanged?.Invoke(this, EventArgs.Empty);
            _browser.Initialize(string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : path);
        }

        public FrameworkElement View { get { return _browser; } }
        public string CurrentPath { get { return _browser.CurrentPath; } }

        public IReadOnlyList<string> SelectedPaths
        {
            get { return _browser.SelectedItems.Select(item => item.FullPath).ToList(); }
        }

        public void NavigateTo(string path)
        {
            _browser.NavigateTo(path);
        }

        public event EventHandler PathChanged;
    }
}
