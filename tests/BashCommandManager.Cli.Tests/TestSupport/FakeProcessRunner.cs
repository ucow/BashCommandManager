using BashCommandManager.Cli.Operations;

namespace BashCommandManager.Cli.Tests.TestSupport;

/// <summary>进程端口的内存替身：记录请求，按配置返回结果或抛异常。</summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    public List<ProcessRunRequest> Requests { get; } = new();

    public ProcessRunResult StartResult { get; set; } = new(true, 4321, null, false);

    public ProcessRunResult RunResult { get; set; } = new(true, 4321, 0, false);

    public Exception? StartException { get; set; }

    public Exception? RunException { get; set; }

    public ProcessRunResult Start(ProcessRunRequest request)
    {
        Requests.Add(request);
        if (StartException is not null)
        {
            throw StartException;
        }
        return StartResult;
    }

    public ProcessRunResult RunWithResult(ProcessRunRequest request)
    {
        Requests.Add(request);
        if (RunException is not null)
        {
            throw RunException;
        }
        return RunResult;
    }
}
