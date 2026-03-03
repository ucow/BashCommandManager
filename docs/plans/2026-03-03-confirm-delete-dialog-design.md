# 删除分组确认对话框设计

## 概述

在删除分组前显示确认对话框，防止用户误操作导致数据丢失。

## 需求

1. 右键点击删除分组时，先弹出确认对话框
2. 对话框应显示分组名称
3. 如果分组下有命令，应提示用户这些命令也将被删除
4. 使用 HandyControl 的 Dialog 控件，保持与现有 UI 风格一致

## 技术方案

### 实现方式

使用 HandyControl 的 `Dialog.Show()` 方法显示确认对话框：

```csharp
var dialog = new ConfirmDialogControl
{
    Message = message,
    Title = "确认删除"
};

var result = await Dialog.Show(dialog).GetResultAsync<bool>();
if (result)
{
    // 执行删除
}
```

### 消息内容

- **分组下有命令时**：
  ```
  分组 "XXX" 下有 N 个命令，确定要删除吗？
  删除后这些命令也将被删除。
  ```

- **分组下无命令时**：
  ```
  确定要删除分组 "XXX" 吗？
  ```

### 界面风格

- 使用 HandyControl 的 `Dialog` 控件
- 按钮：确定/取消
- 图标：警告图标
- 样式与创建分组的输入对话框保持一致

## 实现位置

`ViewModels/GroupTreeViewModel.cs` 中的 `DeleteGroupAsync` 方法

## 验收标准

1. 右键删除分组时显示确认对话框
2. 点击"确定"执行删除
3. 点击"取消"不执行删除
4. 正确显示分组下的命令数量
5. 与现有 UI 风格一致
