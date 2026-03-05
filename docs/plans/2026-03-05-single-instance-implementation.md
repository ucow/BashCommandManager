# 单例启动模式实现计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 实现WPF应用的单例启动模式，当双击exe时如果已有实例运行则激活已有窗口。

**Architecture:** 使用 Mutex 检测是否已有实例，使用命名管道 (NamedPipe) 进行进程间通信，通知主实例激活窗口。

**Tech Stack:** C#, WPF, .NET 8, NamedPipeServerStream/NamedPipeClientStream

---

## Task 1: 创建 SingleInstanceService 服务类

**Files:**
- Create: `Core/Services/SingleInstanceService.cs`

**Step 1: 创建单例服务类**

创建文件 `Core/Services/SingleInstanceService.cs`，内容如下：

```csharp
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BashCommandManager.Core.Services
{
    /// <summary>
    /// 单例启动服务，确保只有一个应用实例在运行
    /// </summary>
    public class SingleInstanceService : IDisposable
    {
        private const string MutexName = "BashCommandManager_SingleInstance_Mutex";
        private const string PipeName = "BashCommandManager_SingleInstance_Pipe";

        private Mutex? _mutex;
        private bool _isFirstInstance;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _pipeServerTask;
        private Action? _onActivateCallback;

        /// <summary>
        /// 是否是第一个实例
        /// </summary>
        public bool IsFirstInstance => _isFirstInstance;

        /// <summary>
        /// 初始化单例服务
        /// </summary>
        /// <param name="onActivateCallback">当收到激活请求时的回调</param>
        /// <returns>如果是第一个实例返回true，否则返回false</returns>
        public bool Initialize(Action onActivateCallback)
        {
            _onActivateCallback = onActivateCallback;

            // 尝试创建全局Mutex
            _mutex = new Mutex(true, MutexName, out _isFirstInstance);

            if (_isFirstInstance)
            {
                // 第一个实例：启动命名管道服务器监听激活请求
                StartPipeServer();
                return true;
            }

            // 不是第一个实例：尝试通知主实例激活
            NotifyFirstInstance();
            return false;
        }

        /// <summary>
        /// 启动命名管道服务器监听激活请求
        /// </summary>
        private void StartPipeServer()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            _pipeServerTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await using var pipeServer = new NamedPipeServerStream(
                            PipeName,
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous);

                        // 等待客户端连接
                        await pipeServer.WaitForConnectionAsync(token);

                        // 读取消息
                        using var reader = new StreamReader(pipeServer);
                        var message = await reader.ReadLineAsync();

                        if (message == "ACTIVATE")
                        {
                            // 在主线程上执行激活回调
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                _onActivateCallback?.Invoke();
                            });
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // 正常取消，退出循环
                        break;
                    }
                    catch (Exception)
                    {
                        // 其他异常，继续监听
                        await Task.Delay(100, token);
                    }
                }
            }, token);
        }

        /// <summary>
        /// 通知第一个实例激活窗口
        /// </summary>
        private void NotifyFirstInstance()
        {
            try
            {
                using var pipeClient = new NamedPipeClientStream(
                    ".",
                    PipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous);

                // 连接管道（超时1秒）
                pipeClient.Connect(1000);

                using var writer = new StreamWriter(pipeClient);
                writer.WriteLine("ACTIVATE");
                writer.Flush();
            }
            catch (Exception)
            {
                // 连接失败，静默处理
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _pipeServerTask?.Wait(TimeSpan.FromSeconds(2));
            _cancellationTokenSource?.Dispose();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
    }
}
```

**Step 2: Commit**

```bash
git add Core/Services/SingleInstanceService.cs
git commit -m "feat: 添加单例启动服务类" -m "Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>"
```

---

## Task 2: 修改 App.xaml.cs 集成单例检测

**Files:**
- Modify: `App.xaml.cs`

**Step 1: 添加单例服务字段**

在 `App` 类中添加字段：

```csharp
private SingleInstanceService? _singleInstanceService;
```

**Step 2: 修改 OnStartup 方法**

将 `OnStartup` 方法修改为以下逻辑：

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    // 初始化单例服务
    _singleInstanceService = new SingleInstanceService();

    // 定义激活回调
    Action activateCallback = () =>
    {
        if (MainWindow != null)
        {
            MainWindow.Show();
            MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
        }
    };

    // 初始化单例服务，如果不是第一个实例则退出
    if (!_singleInstanceService.Initialize(activateCallback))
    {
        // 已有实例在运行，退出当前实例
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
    MainWindow = mainWindow;
    mainWindow.Show();
}
```

**Step 3: 添加 Dispose 处理**

在 `App` 类中添加 `OnExit` 方法：

```csharp
protected override void OnExit(ExitEventArgs e)
{
    _singleInstanceService?.Dispose();
    base.OnExit(e);
}
```

**Step 4: Commit**

```bash
git add App.xaml.cs
git commit -m "feat: 在App.xaml.cs中集成单例启动检测" -m "Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>"
```

---

## Task 3: 验证 MainWindow 支持窗口激活

**Files:**
- Check: `MainWindow.xaml.cs`

**Step 1: 检查 ShowWindow 方法是否可用**

`MainWindow.xaml.cs` 中已经有一个 `ShowWindow()` 方法（第80-85行），该方法实现了：
- `Show()` - 显示窗口
- `WindowState = WindowState.Normal` - 恢复窗口状态
- `Activate()` - 激活窗口到前台

这个方法已经满足需求，不需要额外修改。

---

## Task 4: 构建并测试功能

**Files:**
- All modified files

**Step 1: 构建项目**

```bash
dotnet build -c Release
```

Expected: Build succeeded with 0 errors.

**Step 2: 测试单例启动**

1. 运行生成的exe启动第一个实例
2. 观察应用正常启动
3. 再次双击exe启动第二个实例
4. 观察：
   - 第二个实例应该不显示窗口
   - 第一个实例的窗口应该被激活（置顶显示）

**Step 3: 测试托盘恢复**

1. 启动第一个实例
2. 点击最小化按钮，窗口应该隐藏到托盘
3. 双击exe
4. 观察窗口从托盘恢复并激活

**Step 4: Commit（如果测试通过）**

```bash
git commit --allow-empty -m "test: 验证单例启动模式功能正常" -m "Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>"
```

---

## Task 5: 提交到GitHub

**Step 1: 推送代码**

```bash
git push origin master
```

**Step 2: 创建Release（用户确认测试通过后执行）**

1. 使用GitHub创建新Release
2. 版本号根据当前最新tag递增（如 v1.1.0）
3. 描述说明新增单例启动功能
4. 上传打包好的exe文件

---

## 注意事项

1. Mutex 名称使用了固定的字符串 "BashCommandManager_SingleInstance_Mutex"，这是进程间共享的标识
2. 命名管道超时设置为1秒，如果主实例无响应会静默退出
3. 所有异常都被捕获处理，不会影响用户体验
4. 服务类实现了 IDisposable 接口，确保资源正确释放
