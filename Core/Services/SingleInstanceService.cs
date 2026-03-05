using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BashCommandManager.Core.Services
{
    public class SingleInstanceService : IDisposable
    {
        private const string MutexName = "BashCommandManager_SingleInstance_Mutex_v2";
        private const string PipeName = "BashCommandManager_SingleInstance_Pipe_v2";
        private const string ActivateMessage = "ACTIVATE";
        private const int PipeConnectionTimeoutMs = 1000;

        private Mutex? _mutex;
        private bool _ownsMutex;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _pipeServerTask;
        private Action? _onActivateCallback;

        /// <summary>
        /// 初始化单例服务
        /// </summary>
        public bool Initialize(Action onActivateCallback)
        {
            _onActivateCallback = onActivateCallback;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                // 尝试打开已存在的 Mutex
                try
                {
                    _mutex = Mutex.OpenExisting(MutexName);
                    // Mutex 已存在，说明有另一个实例在运行
                    _ownsMutex = false;
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    // Mutex 不存在，创建新的
                    _mutex = new Mutex(false, MutexName);
                    _ownsMutex = true;
                }

                // 尝试获取 Mutex 所有权
                if (_mutex.WaitOne(0, false))
                {
                    // 成功获取所有权，这是第一个实例
                    _ownsMutex = true;
                    StartPipeServer();
                    return true;
                }
                else
                {
                    // 无法获取所有权，已有实例在运行
                    _ownsMutex = false;
                    _mutex.Close();
                    _mutex = null;
                    NotifyFirstInstance();
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SingleInstanceService 初始化异常: {ex.Message}");
                // 异常时允许启动（降级处理）
                return true;
            }
        }

        /// <summary>
        /// 启动命名管道服务器监听激活消息
        /// </summary>
        private void StartPipeServer()
        {
            _pipeServerTask = Task.Run(async () =>
            {
                while (!_cancellationTokenSource?.IsCancellationRequested ?? false)
                {
                    try
                    {
                        await using var pipeServer = new NamedPipeServerStream(
                            PipeName,
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Message,
                            PipeOptions.Asynchronous);

                        await pipeServer.WaitForConnectionAsync(_cancellationTokenSource?.Token ?? default);

                        using var reader = new StreamReader(pipeServer);
                        string? message = await reader.ReadLineAsync();

                        if (message == ActivateMessage)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                try
                                {
                                    _onActivateCallback?.Invoke();
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"激活回调执行异常: {ex.Message}");
                                }
                            });
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"管道服务器异常: {ex.Message}");
                        await Task.Delay(100);
                    }
                }
            });
        }

        /// <summary>
        /// 向第一个实例发送激活消息
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

                pipeClient.Connect(PipeConnectionTimeoutMs);

                using var writer = new StreamWriter(pipeClient);
                writer.WriteLine(ActivateMessage);
                writer.Flush();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"通知主实例异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();

            try
            {
                _pipeServerTask?.Wait(TimeSpan.FromSeconds(1));
            }
            catch { }

            _cancellationTokenSource?.Dispose();

            if (_mutex != null)
            {
                if (_ownsMutex)
                {
                    try
                    {
                        _mutex.ReleaseMutex();
                    }
                    catch { }
                }
                _mutex.Dispose();
                _mutex = null;
            }
        }
    }
}
