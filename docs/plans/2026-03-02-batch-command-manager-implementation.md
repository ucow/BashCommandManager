# Windows 批处理命令管理器 实施计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 构建一个基于 .NET 8 WPF + HandyControl 的 Windows 批处理命令管理工具

**Architecture:** 采用 MVVM 架构，数据层使用 SQLite + Dapper，UI 使用 HandyControl 控件库，支持分组树形管理和命令执行状态跟踪

**Tech Stack:** .NET 8, WPF, HandyControl, SQLite, Dapper, CommunityToolkit.Mvvm

---

## 任务 1: 创建 WPF 项目结构

**Files:**
- Create: `BashCommandManager.csproj`
- Create: `App.xaml`
- Create: `App.xaml.cs`
- Create: `MainWindow.xaml`
- Create: `MainWindow.xaml.cs`

**Step 1: 创建项目文件**

```bash
dotnet new wpf -n BashCommandManager -o .
```

**Step 2: 添加 NuGet 包引用**

编辑 `BashCommandManager.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="HandyControl" Version="3.4.0" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.2" />
    <PackageReference Include="Dapper" Version="2.1.28" />
    <PackageReference Include="System.Data.SQLite.Core" Version="1.0.118" />
  </ItemGroup>
</Project>
```

**Step 3: 还原包**

```bash
dotnet restore
```

**Step 4: Commit**

```bash
git add .
git commit -m "chore: init WPF project with HandyControl, Dapper, SQLite"
```

---

## 任务 2: 创建核心数据模型

**Files:**
- Create: `Core/Models/Group.cs`
- Create: `Core/Models/Command.cs`

**Step 1: 创建 Group 模型**

```csharp
namespace BashCommandManager.Core.Models;

public class Group
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public int SortOrder { get; set; }

    // 导航属性
    public List<Group> Children { get; set; } = new();
    public List<Command> Commands { get; set; } = new();
}
```

**Step 2: 创建 Command 模型**

```csharp
namespace BashCommandManager.Core.Models;

public enum CommandStatus
{
    Idle,
    Running,
    Completed,
    Failed
}

public class Command
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int GroupId { get; set; }
    public int SortOrder { get; set; }

    // 运行时状态（不持久化）
    public CommandStatus Status { get; set; } = CommandStatus.Idle;
}
```

**Step 3: Commit**

```bash
git add Core/Models/
git commit -m "feat: add Group and Command models"
```

---

## 任务 3: 数据库初始化和服务

**Files:**
- Create: `Infrastructure/DatabaseInitializer.cs`
- Create: `Core/Services/DatabaseService.cs`

**Step 1: 创建数据库初始化器**

```csharp
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Reflection;

namespace BashCommandManager.Infrastructure;

public class DatabaseInitializer
{
    private readonly string _dbPath;

    public DatabaseInitializer()
    {
        var appDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var dataDir = Path.Combine(appDir, "data");
        Directory.CreateDirectory(dataDir);
        _dbPath = Path.Combine(dataDir, "app.db");
    }

    public string ConnectionString => $"Data Source={_dbPath};Version=3;";

    public void Initialize()
    {
        using var connection = new SQLiteConnection(ConnectionString);
        connection.Open();

        var sql = @"
            CREATE TABLE IF NOT EXISTS Groups (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                ParentId INTEGER NULL,
                SortOrder INTEGER DEFAULT 0,
                FOREIGN KEY (ParentId) REFERENCES Groups(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Commands (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Description TEXT,
                FilePath TEXT NOT NULL,
                GroupId INTEGER NOT NULL,
                SortOrder INTEGER DEFAULT 0,
                FOREIGN KEY (GroupId) REFERENCES Groups(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_groups_parent ON Groups(ParentId);
            CREATE INDEX IF NOT EXISTS idx_commands_group ON Commands(GroupId);

            -- 插入根分组
            INSERT OR IGNORE INTO Groups (Id, Name, ParentId, SortOrder)
            VALUES (1, '根分组', NULL, 0);
        ";

        using var command = new SQLiteCommand(sql, connection);
        command.ExecuteNonQuery();
    }
}
```

**Step 2: 创建数据库服务**

```csharp
using System.Data;
using System.Data.SQLite;

namespace BashCommandManager.Core.Services;

public interface IDatabaseService
{
    IDbConnection CreateConnection();
}

public class DatabaseService : IDatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public IDbConnection CreateConnection()
    {
        return new SQLiteConnection(_connectionString);
    }
}
```

**Step 3: Commit**

```bash
git add Infrastructure/DatabaseInitializer.cs Core/Services/DatabaseService.cs
git commit -m "feat: add database initialization and connection service"
```

---

## 任务 4: 数据访问层（Repository）

**Files:**
- Create: `Core/Repositories/GroupRepository.cs`
- Create: `Core/Repositories/CommandRepository.cs`

**Step 1: 创建 GroupRepository**

```csharp
using BashCommandManager.Core.Models;
using Dapper;
using System.Data;

namespace BashCommandManager.Core.Repositories;

public interface IGroupRepository
{
    Task<IEnumerable<Group>> GetAllAsync();
    Task<Group?> GetByIdAsync(int id);
    Task<int> CreateAsync(Group group);
    Task UpdateAsync(Group group);
    Task DeleteAsync(int id);
    Task<IEnumerable<Group>> GetChildrenAsync(int? parentId);
}

public class GroupRepository : IGroupRepository
{
    private readonly IDbConnection _db;

    public GroupRepository(IDbConnection db)
    {
        _db = db;
    }

    public async Task<IEnumerable<Group>> GetAllAsync()
    {
        var sql = "SELECT * FROM Groups ORDER BY SortOrder, Name";
        return await _db.QueryAsync<Group>(sql);
    }

    public async Task<Group?> GetByIdAsync(int id)
    {
        var sql = "SELECT * FROM Groups WHERE Id = @Id";
        return await _db.QueryFirstOrDefaultAsync<Group>(sql, new { Id = id });
    }

    public async Task<int> CreateAsync(Group group)
    {
        var sql = @"
            INSERT INTO Groups (Name, ParentId, SortOrder)
            VALUES (@Name, @ParentId, @SortOrder);
            SELECT last_insert_rowid();";
        return await _db.ExecuteScalarAsync<int>(sql, group);
    }

    public async Task UpdateAsync(Group group)
    {
        var sql = @"
            UPDATE Groups
            SET Name = @Name, ParentId = @ParentId, SortOrder = @SortOrder
            WHERE Id = @Id";
        await _db.ExecuteAsync(sql, group);
    }

    public async Task DeleteAsync(int id)
    {
        var sql = "DELETE FROM Groups WHERE Id = @Id";
        await _db.ExecuteAsync(sql, new { Id = id });
    }

    public async Task<IEnumerable<Group>> GetChildrenAsync(int? parentId)
    {
        var sql = "SELECT * FROM Groups WHERE ParentId = @ParentId ORDER BY SortOrder";
        return await _db.QueryAsync<Group>(sql, new { ParentId = parentId });
    }
}
```

**Step 2: 创建 CommandRepository**

```csharp
using BashCommandManager.Core.Models;
using Dapper;
using System.Data;

namespace BashCommandManager.Core.Repositories;

public interface ICommandRepository
{
    Task<IEnumerable<Command>> GetByGroupIdAsync(int groupId);
    Task<Command?> GetByIdAsync(int id);
    Task<int> CreateAsync(Command command);
    Task UpdateAsync(Command command);
    Task DeleteAsync(int id);
    Task<IEnumerable<Command>> SearchAsync(string keyword);
}

public class CommandRepository : ICommandRepository
{
    private readonly IDbConnection _db;

    public CommandRepository(IDbConnection db)
    {
        _db = db;
    }

    public async Task<IEnumerable<Command>> GetByGroupIdAsync(int groupId)
    {
        var sql = "SELECT * FROM Commands WHERE GroupId = @GroupId ORDER BY SortOrder";
        return await _db.QueryAsync<Command>(sql, new { GroupId = groupId });
    }

    public async Task<Command?> GetByIdAsync(int id)
    {
        var sql = "SELECT * FROM Commands WHERE Id = @Id";
        return await _db.QueryFirstOrDefaultAsync<Command>(sql, new { Id = id });
    }

    public async Task<int> CreateAsync(Command command)
    {
        var sql = @"
            INSERT INTO Commands (Name, Description, FilePath, GroupId, SortOrder)
            VALUES (@Name, @Description, @FilePath, @GroupId, @SortOrder);
            SELECT last_insert_rowid();";
        return await _db.ExecuteScalarAsync<int>(sql, command);
    }

    public async Task UpdateAsync(Command command)
    {
        var sql = @"
            UPDATE Commands
            SET Name = @Name, Description = @Description,
                FilePath = @FilePath, GroupId = @GroupId, SortOrder = @SortOrder
            WHERE Id = @Id";
        await _db.ExecuteAsync(sql, command);
    }

    public async Task DeleteAsync(int id)
    {
        var sql = "DELETE FROM Commands WHERE Id = @Id";
        await _db.ExecuteAsync(sql, new { Id = id });
    }

    public async Task<IEnumerable<Command>> SearchAsync(string keyword)
    {
        var sql = @"
            SELECT * FROM Commands
            WHERE Name LIKE @Keyword OR Description LIKE @Keyword
            ORDER BY Name";
        return await _db.QueryAsync<Command>(sql, new { Keyword = $"%{keyword}%" });
    }
}
```

**Step 3: Commit**

```bash
git add Core/Repositories/
git commit -m "feat: add GroupRepository and CommandRepository with Dapper"
```

---

## 任务 5: 命令执行服务

**Files:**
- Create: `Core/Services/CommandExecutor.cs`

**Step 1: 创建命令执行服务**

```csharp
using BashCommandManager.Core.Models;
using System.Diagnostics;

namespace BashCommandManager.Core.Services;

public interface ICommandExecutor
{
    void Execute(Command command, bool runAsAdmin = false);
    bool IsRunning(int commandId);
    void RegisterProcess(int commandId, Process process);
    void UnregisterProcess(int commandId);
}

public class CommandExecutor : ICommandExecutor
{
    private readonly Dictionary<int, Process> _runningProcesses = new();

    public void Execute(Command command, bool runAsAdmin = false)
    {
        if (!File.Exists(command.FilePath))
        {
            command.Status = CommandStatus.Failed;
            throw new FileNotFoundException($"文件不存在: {command.FilePath}");
        }

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/k \"{command.FilePath}\"",
            WorkingDirectory = Path.GetDirectoryName(command.FilePath),
            UseShellExecute = true
        };

        if (runAsAdmin)
        {
            psi.Verb = "runas";
        }

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.Exited += (s, e) =>
        {
            UnregisterProcess(command.Id);
            command.Status = process.ExitCode == 0 ? CommandStatus.Completed : CommandStatus.Failed;
        };

        process.Start();
        RegisterProcess(command.Id, process);
        command.Status = CommandStatus.Running;
    }

    public bool IsRunning(int commandId) => _runningProcesses.ContainsKey(commandId);

    public void RegisterProcess(int commandId, Process process)
    {
        _runningProcesses[commandId] = process;
    }

    public void UnregisterProcess(int commandId)
    {
        _runningProcesses.Remove(commandId);
    }
}
```

**Step 2: Commit**

```bash
git add Core/Services/CommandExecutor.cs
git commit -m "feat: add command executor with admin support and status tracking"
```

---

## 任务 6: 业务服务层

**Files:**
- Create: `Core/Services/GroupService.cs`
- Create: `Core/Services/CommandService.cs`

**Step 1: 创建 GroupService**

```csharp
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Repositories;

namespace BashCommandManager.Core.Services;

public interface IGroupService
{
    Task<List<Group>> GetGroupTreeAsync();
    Task<Group> CreateGroupAsync(string name, int? parentId);
    Task RenameGroupAsync(int id, string newName);
    Task DeleteGroupAsync(int id, bool cascade = false);
    Task MoveGroupAsync(int id, int? newParentId);
}

public class GroupService : IGroupService
{
    private readonly IGroupRepository _repository;

    public GroupService(IGroupRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<Group>> GetGroupTreeAsync()
    {
        var allGroups = await _repository.GetAllAsync();
        return BuildTree(allGroups.ToList(), null);
    }

    private List<Group> BuildTree(List<Group> groups, int? parentId)
    {
        var result = groups.Where(g => g.ParentId == parentId).ToList();
        foreach (var group in result)
        {
            group.Children = BuildTree(groups, group.Id);
        }
        return result;
    }

    public async Task<Group> CreateGroupAsync(string name, int? parentId)
    {
        var siblings = await _repository.GetChildrenAsync(parentId);
        var group = new Group
        {
            Name = name,
            ParentId = parentId,
            SortOrder = siblings.Count()
        };
        group.Id = await _repository.CreateAsync(group);
        return group;
    }

    public async Task RenameGroupAsync(int id, string newName)
    {
        var group = await _repository.GetByIdAsync(id);
        if (group != null)
        {
            group.Name = newName;
            await _repository.UpdateAsync(group);
        }
    }

    public async Task DeleteGroupAsync(int id, bool cascade = false)
    {
        // SQLite 外键约束会处理级联删除
        await _repository.DeleteAsync(id);
    }

    public async Task MoveGroupAsync(int id, int? newParentId)
    {
        var group = await _repository.GetByIdAsync(id);
        if (group != null)
        {
            group.ParentId = newParentId;
            await _repository.UpdateAsync(group);
        }
    }
}
```

**Step 2: 创建 CommandService**

```csharp
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Repositories;
using Microsoft.Win32;

namespace BashCommandManager.Core.Services;

public interface ICommandService
{
    Task<IEnumerable<Command>> GetByGroupAsync(int groupId);
    Task<Command?> ImportCommandAsync(int groupId);
    Task DeleteCommandAsync(int id);
    Task<IEnumerable<Command>> SearchAsync(string keyword);
}

public class CommandService : ICommandService
{
    private readonly ICommandRepository _repository;

    public CommandService(ICommandRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<Command>> GetByGroupAsync(int groupId)
    {
        return await _repository.GetByGroupIdAsync(groupId);
    }

    public async Task<Command?> ImportCommandAsync(int groupId)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "批处理文件|*.bat;*.cmd|所有文件|*.*",
            Title = "选择批处理文件"
        };

        if (dialog.ShowDialog() == true)
        {
            var filePath = dialog.FileName;
            var command = new Command
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                Description = "",
                FilePath = filePath,
                GroupId = groupId
            };

            command.Id = await _repository.CreateAsync(command);
            return command;
        }

        return null;
    }

    public async Task DeleteCommandAsync(int id)
    {
        await _repository.DeleteAsync(id);
    }

    public async Task<IEnumerable<Command>> SearchAsync(string keyword)
    {
        return await _repository.SearchAsync(keyword);
    }
}
```

**Step 3: Commit**

```bash
git add Core/Services/GroupService.cs Core/Services/CommandService.cs
git commit -m "feat: add GroupService and CommandService business layer"
```

---

## 任务 7: ViewModel 层

**Files:**
- Create: `ViewModels/MainViewModel.cs`
- Create: `ViewModels/GroupTreeViewModel.cs`
- Create: `ViewModels/CommandListViewModel.cs`

**Step 1: 创建 MainViewModel**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BashCommandManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private GroupTreeViewModel _groupTreeViewModel;

    [ObservableProperty]
    private CommandListViewModel _commandListViewModel;

    [ObservableProperty]
    private string _searchKeyword = string.Empty;

    [ObservableProperty]
    private string _statusText = "就绪";

    public MainViewModel(
        GroupTreeViewModel groupTreeViewModel,
        CommandListViewModel commandListViewModel)
    {
        _groupTreeViewModel = groupTreeViewModel;
        _commandListViewModel = commandListViewModel;

        _groupTreeViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(GroupTreeViewModel.SelectedGroup))
            {
                LoadCommandsForGroup();
            }
        };
    }

    private async void LoadCommandsForGroup()
    {
        if (GroupTreeViewModel.SelectedGroup != null)
        {
            await CommandListViewModel.LoadCommandsAsync(GroupTreeViewModel.SelectedGroup.Id);
            UpdateStatus();
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchKeyword))
        {
            if (GroupTreeViewModel.SelectedGroup != null)
            {
                await CommandListViewModel.LoadCommandsAsync(GroupTreeViewModel.SelectedGroup.Id);
            }
        }
        else
        {
            await CommandListViewModel.SearchAsync(SearchKeyword);
        }
        UpdateStatus();
    }

    [RelayCommand]
    private async Task ImportCommandAsync()
    {
        if (GroupTreeViewModel.SelectedGroup == null) return;

        await CommandListViewModel.ImportCommandAsync(GroupTreeViewModel.SelectedGroup.Id);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var groupName = GroupTreeViewModel.SelectedGroup?.Name ?? "无";
        var count = CommandListViewModel.Commands.Count;
        StatusText = $"当前分组: {groupName} | 命令数: {count}";
    }
}
```

**Step 2: 创建 GroupTreeViewModel**

```csharp
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace BashCommandManager.ViewModels;

public partial class GroupTreeViewModel : ObservableObject
{
    private readonly IGroupService _groupService;

    [ObservableProperty]
    private ObservableCollection<Group> _groups = new();

    [ObservableProperty]
    private Group? _selectedGroup;

    public GroupTreeViewModel(IGroupService groupService)
    {
        _groupService = groupService;
    }

    public async Task LoadGroupsAsync()
    {
        var groups = await _groupService.GetGroupTreeAsync();
        Groups = new ObservableCollection<Group>(groups);
    }

    [RelayCommand]
    private async Task CreateGroupAsync(int? parentId)
    {
        var name = $"新建分组_{DateTime.Now:HHmmss}";
        var group = await _groupService.CreateGroupAsync(name, parentId);
        await LoadGroupsAsync();
    }

    [RelayCommand]
    private async Task RenameGroupAsync(int id)
    {
        var newName = $"重命名_{DateTime.Now:HHmmss}";
        await _groupService.RenameGroupAsync(id, newName);
        await LoadGroupsAsync();
    }

    [RelayCommand]
    private async Task DeleteGroupAsync(int id)
    {
        await _groupService.DeleteGroupAsync(id);
        await LoadGroupsAsync();
    }
}
```

**Step 3: 创建 CommandListViewModel**

```csharp
using BashCommandManager.Core.Models;
using BashCommandManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace BashCommandManager.ViewModels;

public partial class CommandListViewModel : ObservableObject
{
    private readonly ICommandService _commandService;
    private readonly ICommandExecutor _executor;

    [ObservableProperty]
    private ObservableCollection<Command> _commands = new();

    public CommandListViewModel(ICommandService commandService, ICommandExecutor executor)
    {
        _commandService = commandService;
        _executor = executor;
    }

    public async Task LoadCommandsAsync(int groupId)
    {
        var commands = await _commandService.GetByGroupAsync(groupId);
        Commands = new ObservableCollection<Command>(commands);
    }

    public async Task SearchAsync(string keyword)
    {
        var commands = await _commandService.SearchAsync(keyword);
        Commands = new ObservableCollection<Command>(commands);
    }

    public async Task ImportCommandAsync(int groupId)
    {
        var command = await _commandService.ImportCommandAsync(groupId);
        if (command != null && Commands.Any())
        {
            Commands.Add(command);
        }
    }

    [RelayCommand]
    private void ExecuteCommand(Command command)
    {
        try
        {
            _executor.Execute(command, runAsAdmin: false);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"执行失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ExecuteAsAdmin(Command command)
    {
        try
        {
            _executor.Execute(command, runAsAdmin: true);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"执行失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DeleteCommandAsync(Command command)
    {
        var result = System.Windows.MessageBox.Show(
            $"确定要删除命令 '{command.Name}' 吗？\n这只会从列表中移除，不会删除实际文件。",
            "确认删除",
            System.Windows.MessageBoxButton.YesNo);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            await _commandService.DeleteCommandAsync(command.Id);
            Commands.Remove(command);
        }
    }
}
```

**Step 4: Commit**

```bash
git add ViewModels/
git commit -m "feat: add MainViewModel, GroupTreeViewModel, CommandListViewModel"
```

---

## 任务 8: 配置 App.xaml 和依赖注入

**Files:**
- Modify: `App.xaml`
- Modify: `App.xaml.cs`

**Step 1: 配置 App.xaml 引入 HandyControl**

```xml
<Application x:Class="BashCommandManager.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml"/>
                <ResourceDictionary Source="pack://application:,,,/HandyControl;component/Themes/Theme.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

**Step 2: 配置 App.xaml.cs 依赖注入**

```csharp
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

    protected override void OnStartup(StartupEventArgs e)
    {
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

        // ViewModels
        services.AddScoped<GroupTreeViewModel>();
        services.AddScoped<CommandListViewModel>();
        services.AddScoped<MainViewModel>();

        // Views
        services.AddScoped<MainWindow>();
    }
}
```

**Step 3: Commit**

```bash
git add App.xaml App.xaml.cs
git commit -m "feat: configure HandyControl theme and dependency injection"
```

---

## 任务 9: 创建主界面

**Files:**
- Modify: `MainWindow.xaml`
- Modify: `MainWindow.xaml.cs`

**Step 1: 创建 MainWindow.xaml**

```xml
<hc:Window x:Class="BashCommandManager.MainWindow"
           xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
           xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
           xmlns:hc="https://handyorg.github.io/handycontrol"
           xmlns:vm="clr-namespace:BashCommandManager.ViewModels"
           Title="批处理命令管理器"
           Height="600" Width="900"
           WindowStartupLocation="CenterScreen">

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- 工具栏 -->
        <ToolBar Grid.Row="0" Margin="5">
            <hc:ButtonGroup>
                <Button Content="导入命令"
                        Command="{Binding ImportCommandCommand}"/>
            </hc:ButtonGroup>
            <Separator/>
            <TextBox x:Name="SearchBox"
                     Width="200"
                     hc:InfoElement.Placeholder="搜索命令..."
                     Text="{Binding SearchKeyword, UpdateSourceTrigger=PropertyChanged}"/>
            <Button Content="搜索"
                    Command="{Binding SearchCommand}"/>
        </ToolBar>

        <!-- 主体内容 -->
        <Grid Grid.Row="1">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="250"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <!-- 左侧分组树 -->
            <Border Grid.Column="0"
                    BorderBrush="{DynamicResource BorderBrush}"
                    BorderThickness="0,0,1,0"
                    Margin="5">
                <TreeView x:Name="GroupTree"
                          ItemsSource="{Binding GroupTreeViewModel.Groups}"
                          SelectedItemChanged="GroupTree_SelectedItemChanged">
                    <TreeView.ItemTemplate>
                        <HierarchicalDataTemplate ItemsSource="{Binding Children}">
                            <StackPanel Orientation="Horizontal">
                                <TextBlock Text="{Binding Name}"/>
                            </StackPanel>
                        </HierarchicalDataTemplate>
                    </TreeView.ItemTemplate>
                    <TreeView.ContextMenu>
                        <ContextMenu>
                            <MenuItem Header="新建子分组"
                                      Command="{Binding DataContext.GroupTreeViewModel.CreateGroupCommand, RelativeSource={RelativeSource AncestorType=TreeView}}"
                                      CommandParameter="{Binding Id}"/>
                            <MenuItem Header="重命名"
                                      Command="{Binding DataContext.GroupTreeViewModel.RenameGroupCommand, RelativeSource={RelativeSource AncestorType=TreeView}}"
                                      CommandParameter="{Binding Id}"/>
                            <Separator/>
                            <MenuItem Header="删除"
                                      Command="{Binding DataContext.GroupTreeViewModel.DeleteGroupCommand, RelativeSource={RelativeSource AncestorType=TreeView}}"
                                      CommandParameter="{Binding Id}"/>
                        </ContextMenu>
                    </TreeView.ContextMenu>
                </TreeView>
            </Border>

            <!-- 右侧命令列表 -->
            <ScrollViewer Grid.Column="1" Margin="10">
                <ItemsControl ItemsSource="{Binding CommandListViewModel.Commands}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <hc:Card Margin="0,5" BorderThickness="1">
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="Auto"/>
                                    </Grid.ColumnDefinitions>

                                    <StackPanel Grid.Column="0" Margin="10">
                                        <TextBlock Text="{Binding Name}"
                                                   FontSize="16"
                                                   FontWeight="Bold"/>
                                        <TextBlock Text="{Binding Description}"
                                                   Foreground="{DynamicResource SecondaryTextBrush}"
                                                   TextTrimming="CharacterEllipsis"/>
                                        <TextBlock Text="{Binding FilePath}"
                                                   FontSize="12"
                                                   Foreground="{DynamicResource TertiaryTextBrush}"
                                                   ToolTip="{Binding FilePath}"/>
                                    </StackPanel>

                                    <StackPanel Grid.Column="1"
                                                Orientation="Horizontal"
                                                VerticalAlignment="Center"
                                                Margin="10">
                                        <Button Content="运行"
                                                Command="{Binding DataContext.CommandListViewModel.ExecuteCommandCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                                CommandParameter="{Binding}"
                                                Margin="0,0,5,0"/>
                                        <Button Content="管理员运行"
                                                Command="{Binding DataContext.CommandListViewModel.ExecuteAsAdminCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                                CommandParameter="{Binding}"
                                                Margin="0,0,5,0"/>
                                        <Button Content="删除"
                                                Command="{Binding DataContext.CommandListViewModel.DeleteCommandCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                                CommandParameter="{Binding}"
                                                Style="{StaticResource ButtonDanger}"/>
                                    </StackPanel>
                                </Grid>
                            </hc:Card>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>
        </Grid>

        <!-- 状态栏 -->
        <StatusBar Grid.Row="2">
            <TextBlock Text="{Binding StatusText}"/>
        </StatusBar>
    </Grid>
</hc:Window>
```

**Step 2: 创建 MainWindow.xaml.cs**

```csharp
using BashCommandManager.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace BashCommandManager;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += async (s, e) =>
        {
            await viewModel.GroupTreeViewModel.LoadGroupsAsync();
        };
    }

    private void GroupTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm && e.NewValue is Core.Models.Group group)
        {
            vm.GroupTreeViewModel.SelectedGroup = group;
        }
    }
}
```

**Step 3: Commit**

```bash
git add MainWindow.xaml MainWindow.xaml.cs
git commit -m "feat: create main window with HandyControl layout"
```

---

## 任务 10: 修复编译问题和测试

**Files:**
- 根据编译错误调整

**Step 1: 构建检查**

```bash
dotnet build
```

修复编译错误（如缺失 using 语句、绑定路径问题等）。

**Step 2: 最终构建测试**

```bash
dotnet build
dotnet run
```

**Step 3: 最终 Commit**

```bash
git add .
git commit -m "feat: complete batch command manager implementation"
```

---

## 完成标准

- [ ] 项目成功编译无错误
- [ ] 应用正常启动并显示主窗口
- [ ] 数据库自动初始化
- [ ] 可以创建分组
- [ ] 可以导入 .bat/.cmd 文件
- [ ] 可以执行命令（普通/管理员）
- [ ] 可以删除命令
