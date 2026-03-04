# HandyControl 弹窗样式改造设计文档

## 概述
将项目中使用的默认 Windows MessageBox 弹窗替换为 HandyControl 样式的弹窗。

## 改造范围

### 需要替换的弹窗

| 文件 | 位置 | 当前弹窗 | 类型 | 替换为 |
|------|------|---------|------|--------|
| `CommandListViewModel.cs` | 第82-85行 | 删除命令确认 | 确认对话框 | `Dialog` + `ConfirmDialogControl` |
| `CommandListViewModel.cs` | 第62行 | 执行失败提示 | 错误提示 | `Growl.Error` |
| `CommandListViewModel.cs` | 第75行 | 执行失败提示(管理员) | 错误提示 | `Growl.Error` |
| `CommandListViewModel.cs` | 第110行 | 目录不存在提示 | 警告提示 | `Growl.Warning` |
| `CommandListViewModel.cs` | 第125行 | 打开目录失败 | 错误提示 | `Growl.Error` |
| `MainViewModel.cs` | 第101行 | 导入成功提示 | 成功提示 | `Growl.Success` |
| `MainViewModel.cs` | 第85行 | 未选择分组提示 | 警告提示 | `Growl.Warning` |
| `MainViewModel.cs` | 第92行 | 虚拟节点提示 | 警告提示 | `Growl.Warning` |
| `MainViewModel.cs` | 第108行 | 导入失败提示 | 错误提示 | `Growl.Error` |

## 设计决策

### 确认类弹窗（需要用户交互）
- 使用 `Dialog.Show(control).GetResultAsync<bool>()`
- 使用现有的 `ConfirmDialogControl` 自定义控件
- 模态对话框，需要用户明确确认或取消

### 提示类弹窗（纯信息展示）
- 使用 `Growl` 系列方法
- `Growl.Success("消息")` - 绿色成功提示
- `Growl.Warning("消息")` - 橙色警告提示
- `Growl.Error("消息")` - 红色错误提示
- 从屏幕右上角滑入，自动消失，不阻塞操作

## 代码示例

### 确认对话框
```csharp
var dialog = new ConfirmDialogControl
{
    Title = "确认删除",
    Message = $"确定要删除命令 '{command.Name}' 吗？"
};
var result = await Dialog.Show(dialog).GetResultAsync<bool>();
if (result) { /* 执行删除 */ }
```

### Growl 提示
```csharp
// 成功
Growl.Success($"成功导入 {count} 个命令");

// 警告
Growl.Warning("请先选择一个分组");

// 错误
Growl.Error($"导入失败: {ex.Message}");
```

## 参考
- 项目已引入 HandyControl 3.5.1
- `GroupTreeViewModel.cs` 中已有使用示例
