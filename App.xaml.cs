using BashCommandManager.Core.Repositories;
using BashCommandManager.Core.Services;
using BashCommandManager.Infrastructure;
using BashCommandManager.Infrastructure.Interop;
using BashCommandManager.ViewModels;
using HandyControl.Controls;
using HandyControl.Data;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Windows;

namespace BashCommandManager;

public partial class App : Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;
    private SingleInstanceService? _singleInstanceService;
    private IGlobalHotkeyService? _hotkeyService;
    private HotkeyToggleCoordinator? _toggleCoordinator;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceService = new SingleInstanceService();

        bool isFirstInstance = _singleInstanceService.Initialize(
            args =>
            {
                if (MainWindow is MainWindow window)
                {
                    window.Show();
                    window.WindowState = WindowState.Normal;
                    window.Activate();
                    window.ApplyStartupArguments(args);
                }
            },
            e.Args);

        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        var services = new ServiceCollection();

        // 初始化数据库
        var initializer = new DatabaseInitializer();
        var migrationWarning = initializer.MigrationWarning;
        if (migrationWarning is not null)
        {
            System.Diagnostics.Debug.WriteLine($"[Database] {migrationWarning}");
        }
        initializer.Initialize();

        // 注册数据库服务
        var dbService = new DatabaseService(initializer.ConnectionString);
        services.AddSingleton<IDatabaseService>(dbService);

        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();

        // 创建主窗口
        var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;  // 赋值给属性以便回调访问
        mainWindow.Show();
        mainWindow.ApplyStartupArguments(e.Args);

        // 迁移告警必须在窗口显示后弹出（Growl 需要已初始化的容器），且任何异常都不能影响启动。
        if (migrationWarning is not null)
        {
            ShowMigrationWarning(migrationWarning, initializer.DatabasePath);
        }

        // 热键宿主是进程级的：注册在这里，而不是主窗口的 Loaded/Closing。
        var settingsService = ServiceProvider.GetRequiredService<ISettingsService>();
        _hotkeyService = ServiceProvider.GetRequiredService<IGlobalHotkeyService>();

        var visibilityController = new MainWindowVisibilityController(mainWindow, new ForegroundWindowActivator());
        _toggleCoordinator = new HotkeyToggleCoordinator(new WindowTogglePolicy(), visibilityController);

        _hotkeyService.HotkeyPressed += OnHotkeyPressed;

        var hotkeySettings = settingsService.GetHotkeySettings();
        if (hotkeySettings.Enabled
            && !_hotkeyService.TryRegister(hotkeySettings.Key, hotkeySettings.Modifiers).IsSuccess)
        {
            Growl.Warning(new GrowlInfo
            {
                Message = "全局快捷键注册失败，可能已被其他程序占用",
                WaitTime = 5
            });
        }
    }

    private static void ShowMigrationWarning(string warning, string databasePath)
    {
        var message = MigrationNotice.Build(warning, databasePath);
        System.Diagnostics.Debug.WriteLine($"[Database] {message}");

        try
        {
            Growl.Warning(new GrowlInfo
            {
                Message = message,
                WaitTime = 8
            });
        }
        catch (Exception ex)
        {
            // 提示失败（如资源未初始化）绝不影响启动。
            System.Diagnostics.Debug.WriteLine($"[Database] 显示迁移告警失败: {ex.Message}");
        }
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        _toggleCoordinator?.Execute();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // 数据库连接
        services.AddScoped<IDbConnection>(sp =>
        {
            var dbService = sp.GetRequiredService<IDatabaseService>();
            return dbService.CreateConnection();
        });

        // Repositories
        services.AddScoped<IGroupRepository, GroupRepository>();
        services.AddScoped<ICommandRepository, CommandRepository>();

        // Services
        services.AddScoped<IGroupService, GroupService>();
        services.AddScoped<ICommandService, CommandService>();
        services.AddSingleton<ICommandExecutor, CommandExecutor>();
        services.AddSingleton<IExecutableLocator, ExecutableLocator>();

        // 新增：设置服务（单例）
        services.AddSingleton<ISettingsService>(_ => new SettingsService());

        // 新增：全局快捷键服务（单例）
        services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();

        // ViewModels
        services.AddScoped<GroupTreeViewModel>();
        services.AddScoped<CommandListViewModel>();
        services.AddScoped<MainViewModel>();
        services.AddScoped<SettingsViewModel>();

        // Views
        services.AddScoped<MainWindow>();
        services.AddScoped<Views.SettingsWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 全进程唯一的热键生命周期注销点。
        if (_hotkeyService is not null)
        {
            _hotkeyService.HotkeyPressed -= OnHotkeyPressed;
            _hotkeyService.Unregister();
            _hotkeyService.Dispose();
            _hotkeyService = null;
        }

        _toggleCoordinator = null;
        _singleInstanceService?.Dispose();
        base.OnExit(e);
    }
}
