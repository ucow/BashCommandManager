namespace BashCommandManager.Cli.Tests.TestSupport;

/// <summary>会改进程级环境变量的测试归到此集合，禁止与其他集合并行，避免互相污染。</summary>
[CollectionDefinition("Environment", DisableParallelization = true)]
public sealed class EnvironmentCollection
{
    public const string Name = "Environment";
}
