using BashCommandManager.Core.Repositories;
using BashCommandManager.Core.Services;
using BashCommandManager.Infrastructure;
using BashCommandManager.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Windows;

namespace BashCommandManager;

public partial class App : Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;
    private SingleInstanceService? _singleInstanceService;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceService = new SingleInstanceService();

        bool isFirstInstance = _singleInstanceService.Initialize(() =>
        {
            if (MainWindow != null)
            {
                MainWindow.Show();
                MainWindow.WindowState = WindowState.Normal;
                MainWindow.Activate();
            }
        });

        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        var services = new ServiceCollection();

        // 初始化数据库
        var initializer = new DatabaseInitializer();
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

        // 新增：设置服务（单例）
        services.AddSingleton<ISettingsService, SettingsService>();

        // 新增：全局快捷键服务（单例）
        services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();

        // ViewModels
        services.AddScoped<GroupTreeViewModel>();
        services.AddScoped<CommandListViewModel>();
        services.AddScoped<MainViewModel>();

        // Views
        services.AddScoped<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceService?.Dispose();
        base.OnExit(e);
    }
}
