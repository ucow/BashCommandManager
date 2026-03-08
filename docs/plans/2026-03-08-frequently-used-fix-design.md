# 常用命令功能修复与空数据提示设计文档

## 问题描述

### 问题 1：常用命令不显示
用户反馈执行命令后，常用命令分组下仍然没有数据展示。

### 问题 2：缺少空数据提示
当命令列表为空时，界面没有任何提示信息。

## 根本原因分析

### 问题 1 根因
在 `CommandRepository.UpdateAsync()` 方法中，SQL UPDATE 语句遗漏了 `ExecutionCount` 和 `LastExecutedAt` 字段的更新：

```sql
-- 原代码（有问题的）
UPDATE Commands
SET Name = @Name, Description = @Description,
    FilePath = @FilePath, GroupId = @GroupId, SortOrder = @SortOrder
WHERE Id = @Id
```

`RecordExecutionAsync` 方法会更新 command 对象的 `ExecutionCount` 和 `LastExecutedAt` 属性，然后调用 `UpdateAsync` 保存到数据库，但由于 SQL 语句缺少这两个字段，更新实际上没有生效。

### 问题 2 根因
XAML 中的 `ItemsControl` 没有配置空数据提示，当命令列表为空时，用户看不到任何反馈。

## 解决方案

### 修复 1：更新 SQL 语句

文件：`Core/Repositories/CommandRepository.cs`

修改 `UpdateAsync` 方法，添加缺失的字段：

```sql
UPDATE Commands
SET Name = @Name, Description = @Description,
    FilePath = @FilePath, GroupId = @GroupId, SortOrder = @SortOrder,
    ExecutionCount = @ExecutionCount, LastExecutedAt = @LastExecutedAt
WHERE Id = @Id
```

### 修复 2：添加空数据提示

#### 2.1 创建 CountToVisibility 转换器

文件：`Infrastructure/Converters/CountToVisibilityConverter.cs`

功能：当计数为 0 时返回 `Visible`，否则返回 `Collapsed`。

#### 2.2 注册转换器

文件：`App.xaml`

添加转换器资源：
```xml
<converters:CountToVisibilityConverter x:Key="CountToVisibility"/>
```

#### 2.3 修改 MainWindow.xaml

使用 HandyControl 的 `hc:Empty` 控件作为空数据提示：

1. 在 `ItemsControl` 外层包裹 `Grid`
2. 为 `ItemsControl` 添加样式，当命令数为 0 时隐藏
3. 添加 `hc:Empty` 控件，当命令数为 0 时显示
4. 为 `hc:Empty` 添加样式触发器，根据当前分组显示不同提示：
   - 常用命令分组："常用命令会显示您执行过的命令"
   - 其他分组："暂无命令"

## 代码变更

### 1. Core/Repositories/CommandRepository.cs
- 修复 `UpdateAsync` 方法，添加 `ExecutionCount` 和 `LastExecutedAt` 到 SQL UPDATE 语句

### 2. Infrastructure/Converters/CountToVisibilityConverter.cs（新增）
- 实现 `IValueConverter` 接口
- `Convert` 方法：count == 0 返回 Visible，否则返回 Collapsed

### 3. App.xaml
- 添加 `CountToVisibilityConverter` 资源引用

### 4. MainWindow.xaml
- 修改命令列表区域布局
- 添加 `ItemsControl` 的空状态样式
- 添加 `hc:Empty` 空数据提示控件

## 测试验证

1. 执行任意命令后，常用命令分组应显示该命令
2. 切换到空分组时，应显示 "暂无命令" 提示
3. 切换到常用命令分组且没有执行记录时，应显示 "常用命令会显示您执行过的命令" 提示
