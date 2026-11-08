# 执行终端可配置化（#9）实施计划评审报告

- 评审对象：`docs/superpowers/plans/2026-11-08-execution-terminal.md`（3661 行，T0 + T1–T8）
- 参照：`docs/superpowers/specs/2026-11-08-execution-terminal-design.md`（300 行）、`docs/superpowers/DECISIONS.md`（105 行）、`docs/superpowers/plans/2026-11-08-hotkey-tray.md`（#8，2502 行）
- 真实现状核对：`Core/Services/CommandExecutor.cs`（64 行）、`Core/Services/SettingsService.cs`（70 行）、`Core/Services/GlobalHotkeyService.cs`、`ViewModels/SettingsViewModel.cs`（115 行）、`ViewModels/CommandListViewModel.cs`（564 行）、`Views/SettingsWindow.xaml`（73 行）、`App.xaml.cs`（95 行）、`BashCommandManager.csproj`（28 行）、`App.xaml`、`Core/Models/Command.cs`、`Core/Models/HotkeySettings.cs`

---

# 结论

**不通过（不满足「可直接执行」的门槛）。**

计划整体质量高：文件清单与现有代码逐一对得上，TDD 顺序（除 T6）、用例到测试方法的映射、DECISIONS D1/D2/D3/D4 的落地都基本正确，B1–B18 / S1–S7 与设计文档逐条对应。但存在 **7 个阻断项**，其中：

- 3 项会**直接编译失败**（CS0104、CS0535/CS0738、CS0246）；
- 2 项会让**测试必然失败**（S8 的异常类型断言、ValidateTemplate 的 `Assert.Single`）；
- 2 项会让 **Task 0 门禁必然失败**（`Resume()` 签名记录错误、`DefaultItemExcludes` 内容与 #8 计划不一致）。

其中 3 项已用本机探针实测确认（见下），不是纸面推断。修法都很小（多数是 1–3 行改动），修完后可转为「有条件通过」。

### 评审方法与证据

1. 逐行通读计划全文 + 设计文档 + DECISIONS + #8 计划相关任务（T1/T2/T5/T8/T9）。
2. 与真实源码逐条比对：行号锚点（`CommandListViewModel.cs:179-213`、`SettingsService.cs` 共 70 行、`CommandExecutor.cs` 共 64 行、`App.xaml.cs:74`、`App.xaml.cs:71-77`、`README.md:46/231`、`CLAUDE.md:112/147`）全部**准确**。
3. 两项实测探针（在临时目录，未触碰仓库）：
   - `File.Move(tmp, <同名目录>, overwrite: true)` 在 **net8.0 运行时**抛 `System.UnauthorizedAccessException`，`ex is IOException == false`（.NET 10 探针结果相同）；用 `FileShare.None` 锁住 tmp 后 `File.WriteAllText` 抛的才是 `System.IO.IOException`。
   - 以 `net8.0-windows` + `HandyControl 3.5.1` 编译 `using HandyControl.Controls; using System.Windows;` + `private void Save(Window? window)`，编译器报 **CS0104**：`"Window"是"HandyControl.Controls.Window"和"System.Windows.Window"之间的不明确的引用`（`HandyControl.Controls.Window` 为 public 类型，`MainWindow.xaml.cs:13` 正继承它）。

---

## 阻断性问题

> 每条给出：位置（计划行号）→ 事实/证据 → 可直接采用的修法。修法均不改计划意图、不改设计文档。

### B-1 `IGlobalHotkeyService.Resume()` 签名与 #8 不一致（编译必失败 + 门禁误导）

- **位置**：计划 122–123（Task 0 Step 3 的期望签名）、2781（`FakeGlobalHotkeyService.Resume`）
- **事实**：#8 计划的最终接口（#8 计划 1538–1556，文件 `Core/Services/IGlobalHotkeyService.cs`）是
  `HotkeyRegistrationResult Resume();`，而 #9 的 T0 写成「`void Suspend();` `void Resume();`」，Fake 也照抄成 `public void Resume() => ResumeCount++;`。
  按 C# 语义，Fake 会同时触发 `CS0535`（未实现接口成员）与 `CS0738`（返回类型不匹配），Task 7 的测试工程无法编译 → 永远到不了 GREEN。
- **修法**：
  1. 计划 122–123 改为：
     ```
     - `void Suspend();` `HotkeyRegistrationResult Resume();`（`IGlobalHotkeyService : IDisposable`）
     ```
  2. 计划 2781 改为：
     ```csharp
     public HotkeyRegistrationResult Resume()
     {
         ResumeCount++;
         return UpdateResult;
     }
     ```
     （`SettingsViewModel` 里 `_hotkeyService.Resume();` 作为语句丢弃返回值是合法的，无需改调用点。）

### B-2 `FakeGlobalHotkeyService` 缺 `using BashCommandManager.Core.Models;`（编译必失败）

- **位置**：计划 2754–2763
- **事实**：#8 把 `HotkeyRegistrationResult` / `HotkeyRegistrationStatus` 放在 **`BashCommandManager.Core.Models`**（#8 计划 866–871 的 `Core/Models/HotkeyRegistrationResult.cs`，`namespace BashCommandManager.Core.Models;`）。#9 的 Fake 只有 `using BashCommandManager.Core.Services;` 与 `using System.Windows.Input;`，因此 `HotkeyRegistrationResult`、`HotkeyRegistrationStatus.Success` 均无法解析（`CS0246`）。
- **修法**：在计划 2755 前加一行，并在 Task 0 Step 3 的记录项里补一句「`HotkeyRegistrationResult`/`HotkeyRegistrationStatus` 的命名空间是 `BashCommandManager.Core.Models`」：
  ```csharp
  using BashCommandManager.Core.Models;
  using BashCommandManager.Core.Services;
  using System.Windows.Input;
  ```

### B-3 `SettingsViewModel.Save/Cancel(Window? window)` 触发 CS0104（主工程编译必失败）

- **位置**：计划 3175 `private void Save(Window? window)`、3268 `private void Cancel(Window? window)`；文件头 3012 `using HandyControl.Controls;` + 3016 `using System.Windows;`
- **事实**：`HandyControl.Controls.Window` 是 public 类型（`MainWindow.xaml.cs:13` 继承它），与 `System.Windows.Window` 同名。实测编译报 `CS0104`。仓库现有代码正是为此写成全限定（`ViewModels/SettingsViewModel.cs:72`、`:111` 均为 `System.Windows.Window?`），#9 的整体替换把这层限定弄丢了。
- **修法**（两行改动，保留其它内容不动）：
  ```csharp
  private void Save(System.Windows.Window? window)
  ...
  private void Cancel(System.Windows.Window? window)
  ```
  （XAML 的 `CommandParameter="{Binding RelativeSource={RelativeSource AncestorType=Window}}"` 传的是 `System.Windows.Window`，绑定与 `[RelayCommand]` 生成的 `SaveCommand`/`CancelCommand` 名称都不受影响。）

### B-4 S8 的异常断言类型错误，测试必然红

- **位置**：计划 1595–1611（S8 用例）、1871（Step 6 的说明文字）
- **事实**：S8 用 `Directory.CreateDirectory(SettingsPath)` 把 `settings.json` 造成目录，再断言 `Assert.ThrowsAny<IOException>(() => service.SaveHotkeySettings(newHotkey))`。实测（net8.0）：此时 `File.Move(tmp, 目录, overwrite: true)` 抛的是 **`UnauthorizedAccessException`**，而 `UnauthorizedAccessException` **不继承 `IOException`**（`IOException : SystemException`，`UnauthorizedAccessException : SystemException`）。所以即使实现完全正确，S8 也一定失败，并且计划 1871 的「两者都是 `IOException`」是错误结论。
- **修法（推荐，确定性）**：把「写失败」改成由 tmp 文件被占用触发（实测抛 `System.IO.IOException`）：
  ```csharp
  [Fact]
  public void S8_写入失败时抛异常但内存值已更新()
  {
      var service = new SettingsService(_directory);
      var tempPath = Path.Combine(_directory, "settings.json.tmp");

      using var locked = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

      var newHotkey = new HotkeySettings
      {
          Enabled = false,
          Key = Key.F5,
          Modifiers = ModifierKeys.Shift
      };

      Assert.ThrowsAny<IOException>(() => service.SaveHotkeySettings(newHotkey));

      Assert.Equal(Key.F5, service.GetHotkeySettings().Key);
      Assert.False(service.GetHotkeySettings().Enabled);
  }
  ```
  同时把 1871 的说明改为：「S8 用 `FileShare.None` 占用 `settings.json.tmp` 制造可预期的 `IOException`；不要用『把 settings.json 建成目录』，那种情况抛的是 `UnauthorizedAccessException`（不是 `IOException`）」。
- **备选修法**（若坚持目录方案）：
  ```csharp
  var ex = Record.Exception(() => service.SaveHotkeySettings(newHotkey));
  Assert.True(ex is IOException || ex is UnauthorizedAccessException);
  ```

### B-5 `ValidateTemplate_未知占位符报错` 的 `Assert.Single` 必然失败

- **位置**：计划 717–724（测试）vs 850–902（实现）
- **事实**：`ValidateTemplate("-Command \"& '{foo}'\"")` 的实现路径是：扫描到 `{foo}` → 追加「未知占位符 `{foo}`…」；循环结束后 `hasFilePlaceholder == false` → 再追加「参数模板必须包含…」。返回 **2 条**错误，而测试断言 `Assert.Single(errors)`（还有 `errors[0]` 的假设）→ 必然失败。注意这是「计划内自带实现下的测试必然失败」，不是实现缺陷。
- **修法**（改一行测试模板，与 B15 第三个用例保持一致）：
  ```csharp
  var errors = TerminalCommandBuilder.ValidateTemplate("{file} {foo}");
  ```
  若希望保留「模板里没有 file 占位符」这一维度，则改成：
  ```csharp
  var errors = TerminalCommandBuilder.ValidateTemplate("-Command \"& '{foo}'\"");
  Assert.Equal(2, errors.Count);
  Assert.Contains(errors, e => e.Contains("{foo}"));
  ```

### B-6 Task 0 的 `DefaultItemExcludes` 门禁与 #8 计划的产物不一致（门禁必然失败）

- **位置**：计划 26（Global Constraints：「主 csproj 已有 `$(DefaultItemExcludes);tests\**;BashCommandManager.Cli\**`（#8 加的）」）、124（Task 0 Step 3 期望 `DefaultItemExcludes` 含 `tests\**` **与** `BashCommandManager.Cli\**`）
- **事实**：#8 计划实际只在 `PropertyGroup` 里加 `$(DefaultItemExcludes);tests\**`（#8 计划 183–198），**没有** `BashCommandManager.Cli\**`。而 DECISIONS D2（第 23–29 行）明确要求三项齐全。于是 #9 的 T0 门禁会「停止并报告」，而计划本身又不允许在 #9 里改 csproj（Global Constraints 25–26）→ 直接卡死。
- **修法**（二选一，推荐 a）：
  - a) 在 #8 落地前把 #8 计划的 183–198 改成 D2 的写法（这是 D2 的锁定决策，且 #10 也依赖它）：
    ```xml
    <DefaultItemExcludes>$(DefaultItemExcludes);tests\**;BashCommandManager.Cli\**</DefaultItemExcludes>
    ```
  - b) 若 #8 已按现状落地且不动它，则把 #9 的门禁改成「必须有 `tests\**`；`BashCommandManager.Cli\**` 由 #10 在同一行追加」，并把 Global Constraints 第 26 行的措辞同步，避免执行者以为 #8 漏做而回改 csproj（那会违反本计划「不改主 csproj」的约束）。

### B-7 `SaveHotkeySettings(HotkeySettings)` 回退了 #8 的 `Clone()` 决策（行为回归）

- **位置**：计划 3240 `_settingsService.SaveHotkeySettings(HotkeySettings);`
- **事实**：#8 计划 Task 9（2281–2282）明确要求 `_settingsService.SaveHotkeySettings(HotkeySettings.Clone());`，理由写在 #8 计划第 80 行（偏差 #3）：`SettingsService.SaveHotkeySettings` 按引用保存入参，若直接传编辑副本，用户在设置页改键后点「取消」会悄悄改掉服务内存中的配置，破坏 A7 与 spec:218。`SettingsViewModel` 在 DI 里实际是「根 scope 单例」（`App.xaml.cs:83` `AddScoped<SettingsViewModel>` + `SettingsWindow.xaml.cs:11` 从 `App.ServiceProvider` 取），所以这条别名问题会真实发生：第一次保存后，界面缓冲区与服务内存对象变成同一个实例。
- **修法**（一行）：
  ```csharp
  _settingsService.SaveHotkeySettings(HotkeySettings.Clone());
  ```
  并在计划 Self-Review §5（3628–3639）补一条「本计划整体替换 `SettingsViewModel`，**保留** #8 Task 9 的 `Clone()` 落盘语义（仅新增执行段逻辑）」，避免后续复核误判成 #8 漏改。

---

## 建议改进

> 以下不阻断执行，但建议在开工前一次性吸收（多数是 1–2 行文字改动）。

### S-1 T6 没有测试，与 D5.4「每任务先失败测试」冲突
计划 2579 写「Test: 无（UI 接线，…）」，但 Self-Review §5（3628–3639）的 10 条偏差里**没有**这一条。建议：要么在 §5 增补一条偏差（说明 `Growl` 依赖已显示窗口、由 T8 冒烟覆盖），要么把 `ExecuteCoreAsync` 里的提示分派抽成可测的纯函数，例如
`internal static string DescribeFailure(ExecutionResult result)`，并在 T6 加 2 条断言（`TerminalConfigurationException → "执行配置无效: …"`、其它 → `"执行失败: …"`）。后者代价 10 行，收益是把 #9 唯一的分支逻辑纳入测试。

### S-2 公开 API 变动未登记（D5.3 / 验收 5）
- `CommandExecutor` 的**隐式无参构造函数被移除**（计划 2415 改为 `(ISettingsService, IExecutableLocator)`）。这是「删除已有公开成员」的字面情形，虽然仓库内唯一消费者是 DI（`App.xaml.cs:71`），仍应在 §5 偏差里记一行。
- `Execute` 在「用户取消 UAC」时由原来的 `Win32Exception(1223)` 改为抛 `OperationCanceledException`（计划 2434），而验收 5 写「原四个成员签名与**语义**不变」。设计文档 §5 是认可这个映射的，但计划没有把它列进偏差表 → 建议在 §5 增补「`Execute` 的异常映射变化（UAC 取消 → `OperationCanceledException`）已获设计 §5 认可」。

### S-3 「原子写」缺少直接断言
spec §2 明确要求 `tmp + Move` 与「写失败不回滚内存」。当前 S5/S6 只验证内容正确，没有任何用例验证：
1. 写失败后**原 `settings.json` 内容不变**；
2. 成功后**没有 `settings.json.tmp` 残留**（实现里 `File.Move` 成功会带走 tmp，但失败路径会留下 tmp）。
建议在 T4 增加 2 条断言（可并入 S8）：失败后 `File.ReadAllText(SettingsPath)` 仍是旧内容；成功后 `Assert.False(File.Exists(tempPath))`。若决定「失败时清理 tmp」，需在 `SaveAll` 里补 `try/finally` 删除。

### S-4 S 用例相对 spec 的覆盖缺口
- spec S5 原文是「先写 Execution 再 `SaveHotkeySettings`；**反向**再存一次」。#9 的 S5（1521–1558）只做了「Execution → Hotkey → 再 Hotkey」，缺「Hotkey → Execution」这一反向顺序。
- spec S3 含「round-trip」，#9 的 S3 只读不写（写入侧由 S5/S6 覆盖），可接受但建议在 S3 末尾补一次 `SaveExecutionSettings` + 重读断言。
- spec §2 步骤 5 要求 `corrupt.bak` **不覆盖**，S4 未覆盖「已存在 corrupt.bak 时保持原内容」。

### S-5 行号锚点在 #8 落地后会漂移
`App.xaml.cs:74`（计划 1384）与 `App.xaml.cs:71-77`（计划 2578）在当前仓库**准确**，但 #8 会在 `OnStartup` 追加约 19 行、加 2 个字段，最终 `ISettingsService` 注册行约在 95 行附近。建议把这两处改成内容锚点（「`// 新增：设置服务（单例）` 下面那一行」/「`services.AddSingleton<ICommandExecutor, CommandExecutor>();` 之后」）。计划 2696 已经这么写了，可以把前面的两处统一。

### S-6 Task 8 Step 5 的 `git status` 期望值会误报
3518–3525 期望「只出现本计划列出的文件」，但仓库当前 `docs/superpowers/plans/` 是未跟踪状态（`git status --short` 输出 `?? docs/superpowers/plans/`）。执行者会看到它而误判失败。建议加一句「`docs/superpowers/plans/` 未跟踪属正常」。

### S-7 Task 8 Step 1 的期望自相矛盾
3456 写「`Skipped: 0`（若显示 `Skipped: 1` 也正确…本计划的 Smoke 测试不产生 `Skipped`）」。Smoke 测试用「提前 return」而非 `Skip`，所以只会是 `Skipped: 0`；建议删掉括号里的 `Skipped: 1` 分支。

### S-8 CLAUDE.md 文档锚点表述矛盾
3443 写「常用命令第 28 行后」，3495 又写「紧跟现有的 `# 构建 Release` 注释行之前」——`CLAUDE.md:28` 正是 `# 构建 Release`，两句话互相矛盾（按后者才是对的）。另 3443 的「开发计划第 147 行后」，实际「已完成：」清单最后一项在 148 行。建议统一成内容锚点。

### S-9 Smoke 测试的时序风险（非阻断，但会让门控后偶发红）
`CommandExecutor.ExecuteWithResult`（计划 2499–2522）先订阅 `Exited`、再 `StartProcess`、最后 `RegisterProcess` + 置 `Running`。若进程在 `RegisterProcess` 之前就退出，`Exited` 里已经置了 `Completed`/`Failed`，随后又被覆盖成 `Running`，且 `_runningProcesses` 会留下一个已死进程（`IsRunning` 泄漏）。`cmd /c` 的退出一般慢于这两步，所以冒烟大概率过，但建议加一行防御：`if (!process.HasExited) { RegisterProcess(...); command.Status = Running; }`。

### S-10 `Build` 的异常契约与设计措辞不一致
设计 §3 写「除 `TerminalConfigurationException` 外不抛异常」，而计划 802–803 对 null 参数抛 `ArgumentNullException`。建议在设计契约里补一句「参数为 null 属调用方错误，不在纯度契约内」，避免后续 reviewer 判定偏差。

### S-11 新增的公开类型 `TerminalKindDisplay` 未在设计/偏差表出现
`GetDisplayName()` 必须是扩展方法，因此需要一个静态类（计划 294）。这是合理实现，但 `TerminalKindDisplay` 是设计文档里没有的公开类型，建议在 §5 偏差补一行（同时确认 #10 链接 `TerminalKind.cs` 时不受影响——它是 WPF-free 的，符合 D2.2）。

### S-12 `SettingsViewModel` 直接调用 `File.Exists` 与 D3 的措辞冲突
D3 写「`SettingsViewModel` 只调用它（ISettingsService），不直接碰文件」。计划 3193 用 `File.Exists(CustomExecutablePath)` 做路径校验——语义上没问题（校验的是终端程序，不是设置文件）。建议在 D3 加一句「校验自定义可执行文件存在性不属于读写设置文件」，避免以后被当作违规。

### S-13 DECISIONS D2.2 的允许链接清单需要补两项
D2.2 第 50 行列了 `TerminalKind.cs`、`ExecutionSettings.cs`、`TerminalCommandBuilder.cs`、`ExecutableLocator.cs`，但没有 #9 新增的 `Core/Models/TerminalLaunchPlan.cs` 与 `Core/Services/ExecutionResult.cs`（`TerminalCommandBuilder` 的返回类型依赖前者，`CommandExecutor` 依赖后者）。#9 的 T8 Step 7（3543）已经写了交接说明，但 DECISIONS 是权威文件，建议同步一行，否则 #10 会按 D2.2 链接而编译失败。

### 已复核、无需改动（供执行者放心）

- **Modify 文件全部存在**：`Core/Services/SettingsService.cs`（70 行，「原 1–70 行」准确）、`Core/Services/CommandExecutor.cs`（64 行，「原 1–64 行」准确）、`ViewModels/CommandListViewModel.cs`（179–213 行与计划 2577 完全一致，且第 3 行确实是 `using BashCommandManager.Core.Services;`）、`ViewModels/SettingsViewModel.cs`、`Views/SettingsWindow.xaml`、`App.xaml.cs`、`README.md`、`CLAUDE.md`。
- **Create 父目录**：`Core/Models/`、`Core/Services/` 已存在；`tests/BashCommandManager.Tests/` 由 #8 创建；`tests/BashCommandManager.Tests/Fakes/` 需新建（用 `write` 类工具会自动建，用 shell 重定向则要先 `mkdir`——建议在 T5 Step 1 补一句 `mkdir tests\BashCommandManager.Tests\Fakes`）。
- **`ISettingsService` 成员完整性**：重写后的 6 个成员（`GetHotkeySettings`/`SaveHotkeySettings`/`HotkeySettingsChanged` + `GetExecutionSettings`/`SaveExecutionSettings`/`ExecutionSettingsChanged`）在计划 1634–1700 全部有实现，`SettingsService : ISettingsService` 不会缺成员；`_appSettings`/`_needsMigration` 非 readonly，`(_appSettings, _needsMigration) = LoadAll();` 合法。
- **`ExecuteWithResult` 与接口/DI/VM 三者自洽**：接口只追加（2394–2403）；`protected virtual StartProcess`（2540）与测试子类（2257/2271/2290）逐字对应；`App.xaml.cs` 的两依赖注册齐备（T4 工厂 + T6 `IExecutableLocator`）；`CommandListViewModel` 生成命令名仍是 `ExecuteCommandCommand`/`ExecuteAsAdminCommand`（`MainWindow.xaml:225/229` 绑定不用改）。
- **`ExecutableLocator` 的 DI 可解析**：`ExecutableLocator` 有两个公开构造函数，但 `Func<...>` 未注册 → 容器只会选中无参构造（`ActivatorUtilities`/`CallSiteFactory` 会跳过无法解析的构造），不会抛 `AmbiguousMatchException`。
- **XAML 绑定名全部存在**（详见覆盖矩阵末表），`BoolToVisibility` 资源键存在（`App.xaml:12`），`ButtonDefault` 样式来自 HandyControl 主题（现有 XAML 已在用）；移除未使用的 `xmlns:hc` 无副作用。
- **测试基础设施依赖正确**：`[Fact]`/`[Theory]`/`[InlineData(枚举常量, 字符串)]`/`[InlineData(null)]` 均合法；`Assert.ThrowsAny<IOException>`、`Assert.Single(IEnumerable<T>)`、`Assert.Contains(collection, predicate)`、`Assert.All`、`Assert.Equal(IEnumerable<T>, IEnumerable<T>)` 在 xunit 2.5.3 都存在；`viewModel.SaveCommand.Execute(null)` 对 `IRelayCommand<Window?>` 传 null 不会抛。
- **不新增 NuGet、不改主 csproj、不改 `HotkeySettings.cs`/`MainWindow.xaml`**：计划内确实没有这类步骤（唯二例外是 B-6 的门禁措辞与 B-7 的 Clone 语义）。

---

## 覆盖矩阵

判定口径：**覆盖** = 有任务/步骤/用例直接对应；**部分** = 有对应但缺一个维度；**缺口** = 没有任何步骤承载。

### A. spec 目标 1–5

| 目标 | 承载任务（计划行号） | 判定 | 备注 |
|---|---|---|---|
| 1. 五种终端 + 参数模板 + 保持打开 + 管理员 | T1 156–162 / T2 784–798 / T7 3290–3420 | 覆盖 | 5 个 `GetDefaultTemplate` 分支 + 5 项 `TerminalOptions` + UI 控件齐全 |
| 2. `%LOCALAPPDATA%\...\settings.json`，旧扁平格式可读、无损迁移 | T4 1626–1850 / S1 1437 / S2 1454 / S6 1560 | 覆盖 | `.v1.bak` 在 `SaveAll` 写入前生成（1805–1819） |
| 3. `TerminalCommandBuilder.Build` 纯函数可单测 | T2 800–969 / B1–B18 / 纯度用例 677–692 | 覆盖 | B17 证明不碰文件系统，677–692 证明同输入等值 |
| 4. 设置窗口「执行」分组；保存后下次执行立即生效；内联校验 | T7 3005–3420 / T5 2445（每次现取） | 覆盖 | 校验三条分支 + `HasError`/`ErrorMessage` 共用 |
| 5. `ICommandExecutor` 旧成员语义不变 + 新增带结果方法；wt/pwsh 降级不崩溃 | T5 2394–2544 / T6 2633–2689 | 部分 | 见 S-2：`Execute` 的 UAC 取消异常类型已变（设计认可但未登记） |

### B. spec 验收标准 1–6

| 验收 | 承载任务 | 判定 | 备注 |
|---|---|---|---|
| 1. 「执行」Tab 控件齐全 + 保存后生效 | T7 XAML 3290–3420 + T5 2445 | 覆盖 | 绑定清单全绿（见 D 表） |
| 2. 旧配置启动无异常、快捷键不丢、首次保存生成 `.v1.bak` 且转嵌套 | T4 S1/S2/S6 | 覆盖 | — |
| 3. B1–B18 + 同输入两次结果相等 | T2 全部 | 覆盖 | B15 断言较宽（计划 3634 已说明）；B-5 修好后 Validate 部分也闭合 |
| 4. wt/pwsh 缺失降级 + Growl 警告；自定义程序缺失保存拦截、运行报错不崩溃 | T5 `终端缺失时降级`(2008)/`自定义程序不存在`(2080)；T6 2657；T7 3184–3199 | 覆盖 | Growl 本身靠 T8 人工冒烟（计划 3633 已说明） |
| 5. 旧 4 成员签名与语义不变、按钮行为不变 | T5 2394–2403；T6 2634–2638 | 部分 | 同 S-2 |
| 6. 主工程正常编译（tests 已排除）、`dotnet test` 全绿 | T8 Step 1–2 | 覆盖 | 前置是 B-6 的门禁修正 |

### C. B1–B18

| 用例 | 测试方法（计划行号） | 判定 |
|---|---|---|
| B1 | `B1_Cmd_保持窗口打开` 439–451 | 覆盖 |
| B2 | `B2_Cmd_中文与空格路径` 453–462 | 覆盖 |
| B3 | `B3_Cmd_不保持窗口打开` 464–472 | 覆盖 |
| B4 | `B4_Cmd_管理员运行` 474–483 | 覆盖 |
| B5 | `B5_WindowsPowerShell_保持窗口打开` 485–495 | 覆盖 |
| B6 | `B6_PowerShell7_保持窗口打开` 497–506 | 覆盖 |
| B7 | `B7_PowerShell_路径含单引号时翻倍` 508–516 | 覆盖 |
| B8 | `B8_WindowsTerminal_保持窗口打开` 518–528 | 覆盖 |
| B9 | `B9_WindowsTerminal_不保持窗口打开` 530–538 | 覆盖 |
| B10 | `B10_WindowsTerminal_管理员运行` 555–563 | 覆盖 |
| B11 | `B11_WindowsTerminal_分号路径被引号包住` 565–574 | 覆盖 |
| B12 | `B12_自定义程序_使用自定义路径与模板` 576–590 | 覆盖 |
| B13 | `B13_自定义程序_fileRaw_原样输出` 592–605 | 覆盖 |
| B14 | `B14_传入已探测的可执行文件时优先使用` 607–616 | 覆盖 |
| B15 | `B15_非法模板抛配置异常` 618–645 | 覆盖（断言偏宽，可接受） |
| B16 | `B16_脚本路径含双引号时抛配置异常` 647–654 | 覆盖 |
| B17 | `B17_磁盘不存在的路径仍返回计划` 656–665 | 覆盖 |
| B18 | `B18_根目录文件的工作目录为盘符根` 667–675 | 覆盖 |
| 纯度 | `相同输入两次调用返回相等结果` 677–692 | 覆盖 |
| Validate | 694–743（5 条） | **部分**：B-5 会让 `ValidateTemplate_未知占位符报错` 必红 |

### D. S1–S8

| 用例 | 测试方法（行号） | 判定 | 备注 |
|---|---|---|---|
| S1 | `S1_旧格式可读且执行段为默认` 1437–1452 | 覆盖 | 断言 `/k {file}` 与 `Key.B`/`Ctrl+Alt` 与真实默认值一致 |
| S2 | `S2_旧格式的自定义快捷键原样保留` 1454–1464 | 覆盖 | `Key=116 → F5`、`Modifiers=4 → Shift` 正确 |
| S3 | `S3_新格式字段一致_缺段用默认_未知字段忽略` 1466–1488 | 覆盖（无 round-trip 写入） | 见 S-4 |
| S4 | `S4_损坏_json_不抛异常并备份为_corrupt_bak` 1490–1504 | 覆盖 | 未验「不覆盖已有 corrupt.bak」 |
| S5 | `S5_两次保存互不抹掉另一段` 1521–1558 | 部分 | 缺设计中的「反向再存一次」（Hotkey→Execution） |
| S6 | `S6_旧格式首次保存生成_v1_bak_且文件变为嵌套格式` 1560–1579 | 覆盖 | 还额外验了 `Key` 仍为数字 |
| S7 | `S7_非法终端与空白模板被归一化` 1581–1593 | 覆盖 | — |
| S8 | `S8_写入失败时抛异常但内存值已更新` 1595–1611 | **缺口（必红）** | 见 B-4 |

### E. 其他 spec 要求

| 要求 | 承载 | 判定 |
|---|---|---|
| spec §4 `ExecutableLocator` 用例（Custom 存在/不存在、`FindOnPath` 4 种、Cmd/PS 永不 null、pwsh 顺序、wt 顺序） | T3 1013–1182 全部 | 覆盖 |
| spec §2 加载算法 5 步 / 序列化 4 选项 / 两个 Save 都写整份 / 立即生效 | T4 1653–1850 + T5 2445 | 覆盖（原子写断言见 S-3） |
| spec §6 校验 3 条 + 保存顺序 + IO 异常提示 | T7 3174–3265（校验在 3177–3207，`ValidateTemplate` 调用 3201–3207；IO 异常 3238–3253） | 覆盖 |
| spec §7 错误表 7 行 | T5 2464–2519；T6 2666–2686；T4 1745–1750 | 覆盖 |
| spec 手工冒烟 6 项 | T8 之后「手工冒烟清单」3652–3661 | 覆盖 |
| spec §测试策略「不执行 dotnet test（设计阶段）」/D6 门槛 | T8 Step 1–2 + 自检清单 3641–3648 | 覆盖（0 warning 依赖主工程，见 S-2/S-9 之外的注意） |

### F. `SettingsWindow.xaml` 绑定 ↔ `SettingsViewModel` 成员（逐项）

| XAML 绑定（计划行号） | VM 成员（计划行号） | 存在 |
|---|---|---|
| `HotkeySettings.Enabled` 3315 | `HotkeySettings` 3031（`HotkeySettings.Enabled`） | 是 |
| `HotkeySettingsDisplay` 3321 | 3034 | 是 |
| `SetHotkeyCommand` 3328 | 3120 | 是 |
| `TerminalOptions` 3345 | 3057 | 是 |
| `Terminal` 3348 | 3043 | 是 |
| `IsCustomTerminal` 3352 | 3066 | 是（`OnTerminalChanged` 里 `OnPropertyChanged`） |
| `CustomExecutablePath` 3360 | 3046 | 是 |
| `BrowseCustomExecutableCommand` 3365 | 3152 | 是 |
| `ArgumentsTemplate` 3370 | 3049 | 是 |
| `ResetTemplateCommand` 3378 | 3168 | 是 |
| `KeepWindowOpen` 3384 | 3052 | 是 |
| `RunAsAdministrator` 3387 | 3055 | 是 |
| `ErrorMessage` 3399 | 3037 | 是 |
| `HasError` 3402 | 3040 | 是 |
| `SaveCommand` + Window 参数 3410–3411 | 3175（**B-3 修好后**） | 是 |
| `CancelCommand` + Window 参数 3414–3415 | 3268（**B-3 修好后**） | 是 |
| `BoolToVisibility` 3352/3402 | `App.xaml:12` `BooleanToVisibilityConverter` | 是 |

---

## 与 #8 计划的接口对照表

| # | 依赖项 | #9 计划期望（行号） | #8 计划实际（行号） | 判定 | 影响 / 修法 |
|---|---|---|---|---|---|
| 1 | 测试项目路径 | `tests/BashCommandManager.Tests/BashCommandManager.Tests.csproj`（25/97） | 同（108） | ✅ | — |
| 2 | 测试项目属性 | `net8.0-windows10.0.19041.0`、`UseWPF=true`、`IsTestProject=true`、`ImplicitUsings=enable`、`Nullable=enable`（101） | 同（123–131） | ✅ | 逐项一致 |
| 3 | 测试包版本 | 17.8.0 / 2.5.3 / 2.5.3（25） | 同（134–139） | ✅ | 与 D2 一致 |
| 4 | `ProjectReference` | `..\..\BashCommandManager.csproj`（101） | 同（143） | ✅ | — |
| 5 | `HotkeySettings.Clone()` | 存在（29/154/3073） | `public HotkeySettings Clone()`（318–326） | ✅ | 名称/可见性一致 |
| 6 | `IGlobalHotkeyService` 文件位置 | `Core/Services/IGlobalHotkeyService.cs`（84，含回退方案） | 新建该文件（1511/1527） | ✅ | 回退分支用不上 |
| 7 | `TryRegister` | Fake 需实现（2775） | `HotkeyRegistrationResult TryRegister(Key, ModifierKeys)`（1540） | ✅ | — |
| 8 | `UpdateHotkey` 返回类型 | `HotkeyRegistrationResult UpdateHotkey(Key, ModifierKeys)`（121） | 同（1542） | ✅ | T7 用 `result.IsSuccess`（3212） |
| 9 | `Suspend()` | `void Suspend();`（122） | `void Suspend();`（1545） | ✅ | — |
| 10 | `Resume()` | **`void Resume();`（122–123）** | **`HotkeyRegistrationResult Resume();`（1548）** | ❌ | **B-1**：T0 记录错 + Fake 编译失败 |
| 11 | 接口 `: IDisposable` | 有（122） | 有（1538） | ✅ | Fake 的 `Dispose()` 满足 |
| 12 | `IsRegistered` | T0 未列；Fake 实现了（2771） | `bool IsRegistered { get; }`（1553） | ✅（Fake 的 `{ get; private set; }` 满足） | — |
| 13 | `HotkeyRegistrationResult` 命名空间 | Fake 只 `using ...Core.Services`（2755） | `BashCommandManager.Core.Models`（866–871） | ❌ | **B-2**：缺 using |
| 14 | `HotkeyRegistrationResult` 形状 | Fake 用 `new(HotkeyRegistrationStatus.Success, Key.B, Ctrl\|Alt)`（2762–2763） | `readonly record struct (Status, Key, Modifiers)` + 5 个静态工厂 + `IsSuccess`（885–917） | ✅ | 主构造器可 `new`，也提供 `Success(...)` 工厂 |
| 15 | `DefaultItemExcludes` 内容 | 含 `tests\**` **与** `BashCommandManager.Cli\**`（26/124） | **仅** `tests\**`（186/198） | ❌ | **B-6**：门禁会停；D2 要求三项 |
| 16 | `DefaultItemExcludes` 位置 | 主 csproj `PropertyGroup`（26） | `PropertyGroup`（198） | ✅ | 位置一致 |
| 17 | `SettingsViewModel` 最终形态 | T7 整体替换（3005–3275） | T9 整体替换（2164–2298） | ⚠️ | 字段/命令/`Suspend`+`Resume`/「先注册后落盘」全部保留 ✅；**`SaveHotkeySettings(HotkeySettings)`（3240）回退了 #8 的 `.Clone()`（2282）→ B-7** |
| 18 | `SettingsViewModel` 命令名 | `SaveCommand`/`CancelCommand`/`SetHotkeyCommand` 不变（2748） | 同 | ✅ | 与 D5.3 一致 |
| 19 | `ISettingsService` 成员 | #8 只用 `GetHotkeySettings`/`SaveHotkeySettings`（2157） | 同 | ✅ | #9 只追加 3 个成员，不破坏 #8 |
| 20 | `App.xaml.cs` 的 `ISettingsService` 注册 | 改成工厂（1384，`App.xaml.cs:74`） | 保留 `services.AddSingleton<ISettingsService, SettingsService>();`（2046） | ✅（内容）/ ⚠️（行号） | #8 之后行号约 +21 → 见 S-5；替换文本仍在，可匹配 |
| 21 | `App.xaml.cs` 的 `IExecutableLocator` 插入点 | 「`AddSingleton<ICommandExecutor, CommandExecutor>();` 之后」（2696–2706） | 同位置（2043） | ✅ | 用内容锚点，行号 2578 会漂移 |
| 22 | `MainWindow.xaml` 命令名 | 保持不变（2567/3646） | 不改 `MainWindow.xaml`（#8 只改 `MainWindow.xaml.cs`） | ✅ | `ExecuteCommandCommand`/`ExecuteAsAdminCommand` 不受影响 |
| 23 | `CommandListViewModel` 构造签名 | 不改（只换两个方法体，2591） | #8 不改该文件 | ✅ | DI 不变 |
| 24 | `_hotkeyService.Resume()` 调用点 | `_hotkeyService.Resume();` 作为语句（3147） | 返回 `HotkeyRegistrationResult`（允许语句丢弃） | ✅ | 无需改（但见 B-1） |
| 25 | Growl 文案 | 「快捷键更新失败，可能已被其他程序占用（已保留原快捷键）」（3214） | 同文案（2265） | ✅ | 逐字一致 |
| 26 | 旧扁平 JSON 形状 | #9 才切换到嵌套（28–29/D3） | #8 明确不改形状（18/2437） | ✅ | 串行顺序成立 |

> 附注（给 #8 执行者的提醒，不属于 #9 的缺陷）：#8 T9 的 `[ObservableProperty] private HotkeySettings _hotkeySettings;` 没有 `= new()` 初始化器，在 `Nullable=enable` 下可能触发 `CS8618`（赋值走的是生成的属性 setter，编译器不追踪）。#9 的 T7 版本写成了 `private HotkeySettings _hotkeySettings = new();`（3031），反而避开了这个警告；#8 若在 T10 要求「不新增警告」，建议在 #8 里也加上初始化器。

---

## 附：建议的开工顺序（把阻断项一次性消掉）

1. 先修 **B-6**（决定 `DefaultItemExcludes` 归谁补），这决定 T0 能否过门禁。
2. 修 **B-1 / B-2**（两处都在 T0 期望与 T7 Fake，5 行以内），T0 的执行者按修正后的签名核对 #8。
3. 修 **B-3 / B-7**（T7 的两处代码：`System.Windows.Window?` 与 `HotkeySettings.Clone()`）。
4. 修 **B-4 / B-5**（T4 的 S8、T2 的 ValidateTemplate 断言），这两处会让测试在「实现正确」的情况下变红，最容易在 TDD 环里把人带偏。
5. 顺带吸收 S-1（T6 偏差登记）、S-3（原子写断言）、S-13（DECISIONS D2.2 补两项）——这三项都会影响后续 #10 的衔接。

---

## 修订记录（作者：修订代理）

- 修订时间：2026-11-08
- 修订对象：`docs/superpowers/plans/2026-11-08-execution-terminal.md`（修订后 3760 行）
- 修订范围：**只改计划文档**；未改源码、未 commit、未改 #8 计划与主 csproj。
- 下文行号均为**修订后**计划文档的当前行号。
- 同步说明：修订期间 #8 计划被并行更新（`SettingsViewModel` 块现为 #8 计划 2393–2575 行，新增 `Key.None` 拦截、`Resume()` 结果分档、`UpdateHotkey` 状态分档文案）。因为 #9 的 T7 是**整文件替换** `SettingsViewModel`，本次同步把 #9 的 T7 块对齐到 #8 的最新形态（保留 #8 的 `SetHotkey`/`Save` 热键逻辑，仅叠加执行段字段/命令/校验），避免执行 #9 时静默回退 #8 的行为。

### 阻断项（B-1 … B-7，全部已修）

| 编号 | 修改位置（小节 + 行号） | 实际改动 |
|---|---|---|
| B-1 | Task 0 Step 3「期望签名」行 122；Task 7 Step 1 Fake `Resume()` 2820–2827；Task 7 Step 5 `SetHotkey` 3166–3226（`var resume = _hotkeyService.Resume();` 3207） | `Resume()` 一律为 `HotkeyRegistrationResult Resume();`（T0 门禁断言同步）；Fake 改为 `public HotkeyRegistrationResult Resume()`，`ResumeCount++` 后 `return UpdateResult;`；调用点不再丢弃返回值：按 #8 最新形态接住 `resume` 并在 `finally` 里按 `Status` 分档（`NotRegistered` 不打扰、其它写 `ErrorMessage` + `Growl.Warning` + `Debug.WriteLine`），并补 `using System.Diagnostics;` |
| B-2 | Task 7 Step 1 `FakeGlobalHotkeyService.cs` 文件头 2794–2797；Task 0 Step 3 记录项 123 | Fake 补 `using BashCommandManager.Core.Models;`；T0 记录 `HotkeyRegistrationResult` / `HotkeyRegistrationStatus` 的命名空间是 `BashCommandManager.Core.Models`，测试 Fake 必须 using |
| B-3 | Task 7 Step 5 提示行 3051、`Save` 3251–3252、`Cancel` 3362–3363 | `private void Save(System.Windows.Window? window)` / `private void Cancel(System.Windows.Window? window)`；并在 Step 5 开头显式说明 `HandyControl.Controls.Window` 与 `System.Windows.Window` 同名会触发 CS0104，禁止「简化」成 `Window?` |
| B-4 | Task 4 Step 3 S8 用例 1598–1626；Task 4 Step 6 期望说明 1911 | 改为 `new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None)` 锁住 `settings.json.tmp` 触发 `IOException`，断言 `Assert.ThrowsAny<IOException>`；额外断言失败后原 `settings.json` 内容不变（原子写）；Step 6 删掉错误的「`File.Move` 到目录抛的也是 `IOException`」结论，改为「`UnauthorizedAccessException` 不是 `IOException`，不要改回目录方案」 |
| B-5 | Task 2 Step 3 `ValidateTemplate_未知占位符报错` 717–725 | 模板改为 `"{file} {foo}"`，`Assert.Single(errors)` 断言恰好 1 条错误；注释说明不带 `{file}` 会多出「必须包含 {file}…」导致 2 条 |
| B-6 | Global Constraints「主工程排除」26；Task 0 Step 3 门禁项 124 | 门禁要求 `DefaultItemExcludes` **同时**含 `tests\**` 与 `BashCommandManager.Cli\**`（DECISIONS D2 的两项），缺任一项即「停止并报告」，并明示不得在本计划里改 csproj。**核查记录**：修订开始（21:18）时本仓库 #8 计划第 186/198 行仍只写 `tests\**`，按指令**未改 #8 计划、未改 csproj**，只把 #9 门禁写全并留了停止条件；修订过程中 #8 计划已被（主代理）更新为含两项（现第 203/217 行），B-6 的前提已成立。主 csproj 仍待 #8 落地 `DefaultItemExcludes` |
| B-7 | Task 7 Step 5 `Save` 3334；Self-Review §5 第 12 条 3737 | `_settingsService.SaveHotkeySettings(HotkeySettings.Clone());`（并保留「先 `UpdateHotkey` 成功再落盘」的顺序与状态分档文案），登记「保留 #8 Task 9 的 Clone 落盘语义」的偏差说明 |

### 建议项（本轮采纳）

| 编号 | 修改位置（小节 + 行号） | 实际改动 |
|---|---|---|
| S-1 | Task 6 Files 2617–2619；Self-Review §5 第 11 条 3736 | T6 登记为 **D5.4 显式豁免**：写明「`Growl` 依赖已显示窗口、单测无法可靠断言」的理由、由 T5 覆盖 `Notice`/`Error`/状态、以及 Task 8 人工冒烟第 3/4/5 条 + 代码复核的手工验证条目；未采用「抽 `DescribeFailure` 纯函数」方案（保持与 #8 的 VM 形态一致） |
| S-3 | Task 4 S6 1574–1577；S8 1624–1625；新增 S9 1628–1651；Self-Review §5 第 1 条 3726；§3 用例表 3695 | S6 断言成功后 `settings.json.tmp` 不残留；S8 断言写失败不破坏原 `settings.json`；新增 S9 直接断言原子写成功路径（tmp 不存在、文件可被 `JsonDocument` 整体解析且以 `}` 结尾、Execution 段为数字枚举） |
| S-5 | Task 4 Files 1385、Task 4 Step 5 1893–1897；Task 6 Files 2618、Task 6 Step 3 2736 | 删除 `App.xaml.cs:74` / `App.xaml.cs:71-77` / 「第 68–71 行附近」等行号锚点，改为「`ConfigureServices` 方法里 `services.AddSingleton<ISettingsService, SettingsService>();` 那一行」「`// Services` 区块里 `services.AddSingleton<ICommandExecutor, CommandExecutor>();` 之后」等符号锚点；未改动文件（`CommandListViewModel.cs:179-213`、`CommandListViewModel.cs` 第 3 行等）的精确行号保留 |
| 用例计数 | File Structure 63（`S1–S9`）；Task 4 Step 3 标题 1517（`S5–S9`）；Task 4 Step 6 1911（`S1–S9 共 9 个用例`）；§3 表 3687–3695（新增 S9 行） | 与 S8 重写 + 新增 S9 对齐，计划内不再出现 `S1–S8` |
| ObservableProperty 记录 | Self-Review §5 第 13 条 3738 | 记录 #8 Task 9 的 `[ObservableProperty] private HotkeySettings _hotkeySettings;` 缺 `= new()`（Nullable=enable 下可能 CS8618）；本计划 T7 保留初始化器，**未改 #8 计划**（由主代理处理） |

### 本轮未采纳的评审建议

- **S-2**（`CommandExecutor` 构造函数移除 / UAC 取消异常映射的偏差登记）：未新增条目；Self-Review §5 第 3 条（测试缝）与第 8 条（错误前缀）已部分覆盖，其余属于设计文档契约表述，超出本轮修订范围。
- **S-4**（S5 反向顺序、S3 round-trip、`corrupt.bak` 不覆盖）：未新增用例；属覆盖增强而非阻断项。
- **S-6 / S-7 / S-8**（`git status` 期望、`Skipped: 0` 措辞、CLAUDE.md 锚点矛盾）：未改；属文档润色，不影响执行正确性。
- **S-9**（`ExecuteWithResult` 先 `RegisterProcess` 再置 `Running` 的时序防御）：未改；属实现代码建议，本轮只改计划文档。
- **S-10 / S-11 / S-12**（`Build` null 契约、`TerminalKindDisplay` 公开类型、`File.Exists` 与 D3 措辞）：未改；属设计/决策文档措辞，未纳入本轮。
- **S-13**（DECISIONS D2.2 允许链接清单）：工作区 `docs/superpowers/DECISIONS.md` 的 D2.2 已有未提交改动、补了 `ExecutionResult.cs`（`git diff` 可见），非本次修订所为。
- **补充发现（建议主代理统一）**：工作区 DECISIONS D2.2 把 `TerminalLaunchPlan` 写成「在 `ExecutionResult.cs` 内」，而 #9 计划 T1/File Structure 把它放在独立文件 `Core/Models/TerminalLaunchPlan.cs`（T8 Step 7 要求 #10 同时链接两者）；两处措辞不一致，需在 DECISIONS 或计划中统一。

### 修订后自检

| 检查 | 命令 | 结果 |
|---|---|---|
| `Resume()` 不再是 `void` | `Select-String -Pattern 'void Resume'` | **0 命中** |
| `Resume()` 调用点接住返回值 | `Select-String -Pattern 'var resume = _hotkeyService\.Resume'` | 1 处（Task 7 `SetHotkey` 3207），带 `if (!resume.IsSuccess)` 分支；不再有裸调用 |
| 保存调用带 `Clone()` | `Select-String -Pattern '_settingsService\.SaveHotkeySettings'` | 1 处（Task 7 `Save` 3334），带 `HotkeySettings.Clone()`；接口/实现/Fake 的 `void SaveHotkeySettings(HotkeySettings settings);` 属声明而非调用，测试中的调用传的都是新建对象 |
| `System.Windows.Window?` ≥ 2 | `Select-String -Pattern 'System\.Windows\.Window\?'` | **3 行**（`Save` 3252、`Cancel` 3363、Step 5 说明 3051），≥2 |
| 无占位词 | `Select-String -Pattern 'TBD\|TODO\|待补充\|类似上文'` | **0 命中**（原 §6 自检清单里引用的占位词示例已改写） |
| 无漂移行号 | `Select-String -Pattern 'App\.xaml\.cs:\d'` | **0 命中** |

