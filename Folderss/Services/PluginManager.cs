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

        private static string AppDataDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Folderss"); }
        }

        public static string PluginsDirectory { get { return Path.Combine(AppDataDirectory, "plugins"); } }
        private static string ExtractRoot { get { return Path.Combine(PluginsDirectory, "extracted"); } }
        private static string DataRoot { get { return Path.Combine(AppDataDirectory, "plugin-data"); } }

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
                return loaded;
            }
            catch (Exception ex)
            {
                error = Describe(ex);
                return null;
            }
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

            if (ReportedAssemblies.Add(assembly))
            {
                var context = AssemblyLoadContext.GetLoadContext(assembly) as PluginLoadContext;
                ShowError(context?.PluginId ?? assembly.GetName().Name,
                    "플러그인에서 오류가 발생했습니다. 이 플러그인의 이후 오류는 표시하지 않습니다. 문제가 계속되면 플러그인을 제거하세요.",
                    Describe(exception));
            }
            return true;
        }

        private static Assembly FindPluginAssembly(Exception exception)
        {
            for (var ex = exception; ex != null; ex = ex.InnerException)
            {
                foreach (var frame in new StackTrace(ex).GetFrames())
                {
                    var assembly = frame.GetMethod()?.DeclaringType?.Assembly;
                    if (assembly != null && AssemblyLoadContext.GetLoadContext(assembly) is PluginLoadContext)
                        return assembly;
                }
            }
            return null;
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
