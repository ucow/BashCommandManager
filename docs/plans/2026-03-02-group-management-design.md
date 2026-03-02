# 分组管理功能设计文档

## 概述

实现 TreeView 分组右键菜单功能：新建子分组、重命名、删除。

## 需求

| 功能 | 要求 |
|------|------|
| 新建子分组 | 弹出输入框让用户先输入名称 |
| 重命名 | 右键菜单选择"重命名"，就地编辑 |
| 删除分组 | 提示确认，可选择同时删除或取消 |

## 方案：自定义 EditableTextBlock 控件

## 架构设计

```
┌─────────────────────────────────────────────────────────────┐
│                      MainWindow.xaml                        │
│  ┌───────────────────────────────────────────────────────┐  │
│  │                    TreeView                           │  │
│  │  ┌─────────────────────────────────────────────────┐  │  │
│  │  │  HierarchicalDataTemplate                       │  │  │
│  │  │  ┌───────────────────────────────────────────┐  │  │  │
│  │  │  │   local:EditableTextBlock                 │  │  │  │
│  │  │  │   ├── TextBlock (显示模式)                 │  │  │  │
│  │  │  │   └── TextBox   (编辑模式)                 │  │  │  │
│  │  │  └───────────────────────────────────────────┘  │  │  │
│  │  └─────────────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│              Infrastructure/Controls/EditableTextBlock      │
│  自定义控件：封装显示/编辑切换逻辑，支持双击、Enter、ESC等    │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                ViewModels/GroupTreeViewModel.cs             │
│  - CreateGroupCommand  → 弹出 Dialog 输入名称               │
│  - RenameGroupCommand  → 触发就地编辑模式                   │
│  - DeleteGroupCommand  → 弹出确认 MessageBox                │
└─────────────────────────────────────────────────────────────┘
```

## 组件设计

### EditableTextBlock 自定义控件

**文件位置：** `Infrastructure/Controls/EditableTextBlock.xaml` + `.cs`

**依赖属性：**
- `Text`：绑定的文本内容
- `IsEditing`：是否处于编辑模式
- `Command`：编辑完成时的命令（可选）

**交互逻辑：**
| 操作 | 行为 |
|------|------|
| 双击 TextBlock | 进入编辑模式 |
| 按 Enter | 保存并退出编辑模式 |
| 按 ESC | 取消编辑，恢复原值 |
| 失去焦点 | 自动保存并退出编辑模式 |

### 右键菜单

```xml
<ContextMenu>
    <MenuItem Header="新建子分组" Command="{Binding CreateGroupCommand}" .../>
    <MenuItem Header="重命名" Command="{Binding RenameGroupCommand}" .../>
    <Separator/>
    <MenuItem Header="删除" Command="{Binding DeleteGroupCommand}" .../>
</ContextMenu>
```

### ViewModel 命令

**新建分组：**
- 弹出 HandyControl `Dialog` 输入框
- 用户输入名称 → 创建分组 → 刷新树

**重命名：**
- 通过 CommandParameter 传递 Group 对象
- 设置 Group.IsEditing = true（触发就地编辑）
- 编辑完成后调用 GroupService.RenameGroupAsync

**删除分组：**
- 检查分组下是否有命令
- 弹出确认对话框，显示命令数量
- 用户确认后删除，取消则不操作

## 数据流

```
新建分组流程：
用户右键 → 点击"新建子分组" → 弹出Dialog输入名称
    → ViewModel.CreateGroupAsync(name, parentId)
    → GroupService.CreateGroupAsync
    → 刷新 Groups 集合

重命名流程：
用户右键 → 点击"重命名" → Group.IsEditing = true
    → EditableTextBlock 切换到编辑模式
    → 用户输入新名称 → 按Enter或失焦
    → Group.IsEditing = false
    → ViewModel.RenameGroupAsync(id, newName)
    → 刷新 Groups 集合

删除流程：
用户右键 → 点击"删除" → 检查子命令数量
    → 弹出确认对话框："该分组下有X个命令，确定删除？"
    → 用户确认 → GroupService.DeleteGroupAsync
    → 刷新 Groups 集合
```

## 错误处理

| 场景 | 处理 |
|------|------|
| 名称为空 | 显示提示"分组名称不能为空" |
| 名称重复 | 显示提示"该分组名称已存在" |
| 删除失败 | 显示错误信息，保持界面状态 |

## 需要修改的文件

1. **新增：** `Infrastructure/Controls/EditableTextBlock.xaml` + `.cs`
2. **修改：** `Core/Models/Group.cs`（添加 IsEditing 属性）
3. **修改：** `ViewModels/GroupTreeViewModel.cs`（更新命令逻辑）
4. **修改：** `MainWindow.xaml`（使用 EditableTextBlock）

## 决策记录

- **方案选择：** 方案B（自定义 EditableTextBlock 控件）
- **重命名触发：** 右键菜单选择"重命名"
- **新建分组：** 弹出对话框输入名称
- **删除处理：** 提示确认，可选择删除
