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
        private const string MutexName = "BashCommandManager_SingleInstance_Mutex";
        private const string PipeName = "BashCommandManager_SingleInstance_Pipe";
        private const string ActivateMessage = "ACTIVATE";
        private const int PipeConnectionTimeoutMs = 1000;

        private Mutex? _mutex;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _pipeServerTask;
        private Action? _onActivateCallback;
        private bool _isFirstInstance;
        private bool _disposed;

        /// <summary>
        /// 初始化单例服务
        /// </summary>
        /// <param name="onActivateCallback">当收到激活消息时的回调（在主实例中执行）</param>
        /// <returns>如果是第一个实例返回 true，否则返回 false</returns>
        public bool Initialize(Action onActivateCallback)
        {
            _onActivateCallback = onActivateCallback;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                // 尝试创建 Mutex
                _mutex = new Mutex(false, MutexName, out bool createdNew);
                _isFirstInstance = createdNew;

                if (createdNew)
                {
                    // 第一个实例：启动命名管道服务器监听
                    StartPipeServer();
                    return true;
                }
                else
                {
                    // 不是第一个实例：向主实例发送激活消息
                    NotifyFirstInstance();
                    return false;
                }
            }
            catch (Exception ex)
            {
                // 异常处理：默认允许启动
                System.Diagnostics.Debug.WriteLine($"SingleInstanceService 初始化异常: {ex.Message}");
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

                        // 等待客户端连接
                        await pipeServer.WaitForConnectionAsync(_cancellationTokenSource?.Token ?? default);

                        // 读取消息
                        using var reader = new StreamReader(pipeServer);
                        string? message = await reader.ReadLineAsync();

                        if (message == ActivateMessage)
                        {
                            // 在主线程执行回调
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
                        // 正常取消，退出循环
                        break;
                    }
                    catch (IOException ex)
                    {
                        // 管道断开或其他IO错误
                        System.Diagnostics.Debug.WriteLine($"管道服务器 IO 异常: {ex.Message}");
                        await Task.Delay(100);
                    }
                    catch (Exception ex)
                    {
                        // 其他异常，等待后重试
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

                // 连接到服务器，设置超时
                pipeClient.Connect(PipeConnectionTimeoutMs);

                using var writer = new StreamWriter(pipeClient);
                writer.WriteLine(ActivateMessage);
                writer.Flush();
            }
            catch (TimeoutException)
            {
                // 连接超时，可能是主实例正在启动中
                System.Diagnostics.Debug.WriteLine("连接到主实例超时");
            }
            catch (IOException ex)
            {
                // 管道不存在或其他IO错误
                System.Diagnostics.Debug.WriteLine($"通知主实例 IO 异常: {ex.Message}");
            }
            catch (Exception ex)
            {
                // 其他异常
                System.Diagnostics.Debug.WriteLine($"通知主实例异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                // 取消管道服务器任务
                _cancellationTokenSource?.Cancel();

                // 等待管道服务器任务完成
                if (_pipeServerTask != null && !_pipeServerTask.IsCompleted)
                {
                    try
                    {
                        _pipeServerTask.Wait(TimeSpan.FromSeconds(1));
                    }
                    catch (AggregateException)
                    {
                        // 忽略取消异常
                    }
                }

                _cancellationTokenSource?.Dispose();

                // 释放 Mutex
                if (_mutex != null)
                {
                    if (_isFirstInstance)
                    {
                        _mutex.ReleaseMutex();
                    }
                    _mutex.Dispose();
                    _mutex = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SingleInstanceService 释放资源异常: {ex.Message}");
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}
