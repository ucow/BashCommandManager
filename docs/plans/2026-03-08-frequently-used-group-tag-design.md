# 常用命令分组标签展示设计

## 需求概述

在常用命令页面中，为每个命令卡片添加分组信息的展示，使用 HandyControl 的 Tag 控件，与命令名称同行显示。

## 现状分析

### 已有实现
- 常用命令通过虚拟节点（Id=-1）展示，按执行次数降序排列
- `CommandRepository.GetFrequentlyUsedAsync()` 已通过 JOIN 查询获取 `GroupName`
- `MainWindow.xaml` 第 203-208 行已有简单的 `[GroupName]` 文本显示

### 当前代码位置
- 命令卡片模板：`MainWindow.xaml` 第 154-235 行
- 分组标签现有实现：第 203-208 行

## 设计方案

### UI 布局

**命令名称区域新结构**：
```
┌─────────────────────────────────────────────────────────────┐
│ 命令名称 [分组标签]                                          │
│ 命令描述                                                     │
│ 📄 文件路径                                                  │
└─────────────────────────────────────────────────────────────┘
```

### XAML 修改

将现有的分组标签 `TextBlock`（第 203-208 行）替换为 `hc:Tag`：

```xml
<StackPanel Orientation="Horizontal" VerticalAlignment="Center">
    <TextBlock Text="{Binding Name}"
               FontSize="16"
               FontWeight="Bold"/>
    <!-- 使用 HandyControl Tag 控件展示分组 -->
    <hc:Tag Content="{Binding GroupName}"
            Margin="8,0,0,0"
            ShowCloseButton="False"
            Visibility="{Binding GroupName, Converter={StaticResource StringToVisibility}}"/>
</StackPanel>
```

### 移除旧代码

删除原有的简单文本分组标签（第 203-208 行）：
```xml
<!-- 待删除的旧代码 -->
<TextBlock Text="{Binding GroupName, StringFormat=' [{}]'}">
```

### 排序策略

保持现有排序方式：
- 按 `ExecutionCount DESC` 排序
- 其次按 `LastExecutedAt DESC` 排序

## 技术要点

### 数据绑定
- `GroupName` 已在 `Command` 模型中定义（非持久化字段）
- `CommandRepository.GetFrequentlyUsedAsync()` 已通过 LEFT JOIN Groups 表获取分组名称

### HandyControl Tag 控件
- 使用默认样式，自动适配主题
- `ShowCloseButton="False"` 隐藏关闭按钮（纯展示用途）
- `Visibility` 绑定确保空分组名时不显示

## 验收标准

- [ ] 常用命令页面每个命令卡片显示分组标签
- [ ] 分组标签使用 `hc:Tag` 控件，样式与 HandyControl 一致
- [ ] 标签位于命令名称右侧，间距 8px
- [ ] 分组名为空时不显示标签
- [ ] 移除原有的 `[GroupName]` 文本显示
- [ ] 常用命令排序保持不变（按执行次数）
