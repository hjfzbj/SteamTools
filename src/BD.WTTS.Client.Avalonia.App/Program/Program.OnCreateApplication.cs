// ReSharper disable once CheckNamespace
namespace BD.WTTS;

static partial class Program
{
    static bool isOnCreateAppExecuting = false;

    /// <summary>
    /// 在创建 App 前执行的初始化
    /// </summary>
    /// <param name="isTrace"></param>
    static void OnCreateAppExecuting(bool isTrace = false)
    {
        if (isOnCreateAppExecuting) return;
        isOnCreateAppExecuting = true;

        // 监听当前应用程序域的程序集加载
        AppDomain.CurrentDomain.AssemblyLoad += CurrentDomain_AssemblyLoad;
        static void CurrentDomain_AssemblyLoad(object? sender, AssemblyLoadEventArgs args)
        {
#if DEBUG
            Console.WriteLine($"loadasm: {args.LoadedAssembly}, location: {args.LoadedAssembly.Location}");
#endif
            // 使用 native 文件夹导入解析本机库
            try
            {
                NativeLibrary.SetDllImportResolver(args.LoadedAssembly, GlobalDllImportResolver.Delegate);
            }
            catch
            {
                // ArgumentNullException assembly 或 resolver 为 null。
                // ArgumentException 已为此程序集设置解析程序。
                // 此每程序集解析程序是第一次尝试解析此程序集启动的本机库加载。
                // 此方法的调用方应仅为自己的程序集注册解析程序。
                // 每个程序集只能注册一个解析程序。 尝试注册第二个解析程序失败并出现 InvalidOperationException。
                // https://learn.microsoft.com/zh-cn/dotnet/api/system.runtime.interopservices.nativelibrary.setdllimportresolver
            }
        }

        AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        static Assembly? CurrentDomain_AssemblyResolve(object? sender, ResolveEventArgs args)
        {
            try
            {
                var fileNameWithoutEx = args.Name.Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrEmpty(fileNameWithoutEx))
                {
                    var isResources = fileNameWithoutEx.EndsWith(".resources");
                    if (isResources)
                    {
                        // System.Composition.Convention.resources
                        // 已包含默认资源，通过反射调用已验证成功
                        // typeof(ConventionBuilder).Assembly.GetType("System.SR").GetProperty("ArgumentOutOfRange_InvalidEnumInSet", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)
                        return null;
                    }
                    // 当前根目录下搜索程序集
                    var filePath = Path.Combine(AppContext.BaseDirectory, $"{fileNameWithoutEx}.dll");
                    if (File.Exists(filePath)) return Assembly.LoadFile(filePath);
                    // 当前根目录下独立框架运行时中搜索程序集
                    filePath = Path.Combine(AppContext.BaseDirectory, "..", "dotnet", "shared", "Microsoft.AspNetCore.App", Environment.Version.ToString(), $"{fileNameWithoutEx}.dll");
                    if (File.Exists(filePath)) return Assembly.LoadFile(filePath);
                    // 当前已安装的运行时
                    filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.AspNetCore.App", Environment.Version.ToString(), $"{fileNameWithoutEx}.dll");
                    if (File.Exists(filePath)) return Assembly.LoadFile(filePath);
                    var dotnet_root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
                    if (!string.IsNullOrWhiteSpace(dotnet_root) && Directory.Exists(dotnet_root))
                    {
                        filePath = Path.Combine(dotnet_root, "shared", "Microsoft.AspNetCore.App", Environment.Version.ToString(), $"{fileNameWithoutEx}.dll");
                        if (File.Exists(filePath)) return Assembly.LoadFile(filePath);
                    }

                }
            }
            catch
            {

            }
#if DEBUG
            Console.WriteLine($"asm-resolve fail, name: {args.Name}");
#endif
            return null;
        }

        bool isDesignMode = Design.IsDesignMode;

        if (isTrace) StartWatchTrace.Record();
        try
        {
#if MACOS || MACCATALYST || IOS
            MacCatalystFileSystem.InitFileSystem();
#elif LINUX
            LinuxFileSystem.InitFileSystem();
#elif WINDOWS && !WINDOWS_DESKTOP_BRIDGE
            WindowsFileSystem.InitFileSystem();
#elif ANDROID
            FileSystemEssentials.InitFileSystem();
#else
            FileSystem2.InitFileSystem();
#endif
            Repository.DataBaseDirectory = IOPath.AppDataDirectory;
            if (isTrace) StartWatchTrace.Record("FileSystem");

            IApplication.InitLogDir(isTrace: isTrace);
            if (isTrace) StartWatchTrace.Record("InitLogDir");

            GlobalExceptionHelpers.Init();
            if (isTrace) StartWatchTrace.Record("ExceptionHandler");

            if (!isDesignMode)
            {
#if DBREEZE
                SettingsProviderV3.Migrate();
                if (isTrace) StartWatchTrace.Record("SettingsHost.Migrate");
                PreferencesPlatformServiceImplV2.Migrate();
                if (isTrace) StartWatchTrace.Record("Preferences.Migrate");
#endif
                SettingsHost.Load();
                if (isTrace) StartWatchTrace.Record("SettingsHost");
            }
        }
        finally
        {
            if (isTrace) StartWatchTrace.Record(dispose: true);
        }
    }

    /// <summary>
    /// 在创建 App 时执行的初始化
    /// </summary>
    /// <param name="host"></param>
    /// <param name="handlerViewModelManager"></param>
    /// <param name="isTrace"></param>
    static void OnCreateAppExecuted(IApplication.IProgramHost host, Action<IViewModelManager>? handlerViewModelManager = null, bool isTrace = false)
    {
        if (isTrace) StartWatchTrace.Record();
        try
        {
            host.Application.PlatformInitSettingSubscribe();
            if (isTrace) StartWatchTrace.Record("SettingSubscribe");

            var vmService = IViewModelManager.Instance;
            vmService.InitViewModels();
            if (isTrace) StartWatchTrace.Record("VM.Init");

            handlerViewModelManager?.Invoke(vmService);
            vmService.MainWindow?.Initialize();
            if (isTrace) StartWatchTrace.Record("VM.Delegate");
        }
        finally
        {
            if (isTrace) StartWatchTrace.Record(dispose: true);
        }
    }
}