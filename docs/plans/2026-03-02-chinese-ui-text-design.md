# 界面文本中文化设计方案

## 背景

项目当前 MainWindow.xaml 中的界面文本使用英文，与 ViewModels 中的中文状态文本不统一。需要将所有界面文本统一为中文。

## 目标

将 MainWindow.xaml 中的所有用户可见文本从英文改为中文，保持与 ViewModels 中文本风格一致。

## 修改清单

### MainWindow.xaml

| 行号 | 原文 | 修改后 |
|------|------|--------|
| 6 | `Title="Batch Command Manager"` | `Title="批处理命令管理器"` |
| 19 | `Content="Import Command"` | `Content="导入命令"` |
| 25 | `Placeholder="Search commands..."` | `Placeholder="搜索命令..."` |
| 27 | `Content="Search"` | `Content="搜索"` |
| 53 | `Header="New Subgroup"` | `Header="新建子分组"` |
| 56 | `Header="Rename"` | `Header="重命名"` |
| 60 | `Header="Delete"` | `Header="删除"` |
| 96 | `Content="Run"` | `Content="运行"` |
| 100 | `Content="Run as Admin"` | `Content="管理员运行"` |
| 104 | `Content="Delete"` | `Content="删除"` |

## 技术要点

1. **文件编码**: 确保 MainWindow.xaml 保存为 UTF-8 编码（带 BOM），避免中文乱码
2. **无需修改 ViewModels**: MainViewModel.cs 中的状态文本已经是中文
3. **构建验证**: 修改后执行 `dotnet build` 确保无编译错误

## 验收标准

- [ ] MainWindow.xaml 中所有英文文本已改为中文
- [ ] 项目能正常编译无错误
- [ ] 界面显示中文正常，无乱码
