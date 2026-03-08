# 常用命令分组标签展示实现计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 在常用命令页面使用 HandyControl 的 Tag 控件展示命令所属分组信息

**Architecture:** 修改 MainWindow.xaml 中的命令卡片模板，将原有的 `[GroupName]` 文本替换为 `hc:Tag` 控件，与命令名称同行显示

**Tech Stack:** WPF, HandyControl 3.5.1, XAML

---

## 前置条件

- 项目已正确配置 HandyControl 命名空间 `xmlns:hc="https://handyorg.github.io/handycontrol"`
- `GroupName` 属性已在 `Command` 模型中定义
- `StringToVisibility` 转换器已存在

---

### Task 1: 修改命令卡片中的分组标签显示

**Files:**
- Modify: `MainWindow.xaml:172-183` (命令名称区域)
- Modify: `MainWindow.xaml:203-209` (删除旧的分组标签)

**Step 1: 查看当前命令卡片结构**

阅读 `MainWindow.xaml` 第 172-210 行，确认命令名称 StackPanel 的结构：

```bash
cat -n MainWindow.xaml | sed -n '172,210p'
```

**Step 2: 修改命令名称 StackPanel**

将第 172-175 行的命令名称区域改为水平排列，并添加 Tag 控件：

**Current (line 172-175):**
```xml
<StackPanel Grid.Column="1" Margin="10">
    <TextBlock Text="{Binding Name}"
               FontSize="16"
               FontWeight="Bold"/>
```

**New:**
```xml
<StackPanel Grid.Column="1" Margin="10">
    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
        <TextBlock Text="{Binding Name}"
                   FontSize="16"
                   FontWeight="Bold"
                   VerticalAlignment="Center"/>
        <hc:Tag Content="{Binding GroupName}"
                Margin="8,0,0,0"
                ShowCloseButton="False"
                Visibility="{Binding GroupName, Converter={StaticResource StringToVisibility}}"
                VerticalAlignment="Center"/>
    </StackPanel>
```

**Step 3: 删除旧的分组标签代码**

删除第 203-208 行的旧分组标签（在文件路径 StackPanel 中）：

```xml
<!-- 删除这段代码 -->
<TextBlock Text="{Binding GroupName, StringFormat=' [{}]'}"
           FontSize="12"
           Foreground="{DynamicResource PrimaryBrush}"
           Margin="5,0,0,0"
           Visibility="{Binding GroupName, Converter={StaticResource StringToVisibility}}"/>
```

**Step 4: 验证 XAML 语法**

运行构建检查 XAML 是否正确：

```bash
dotnet build -c Debug
```

Expected: BUILD SUCCESS

**Step 5: 运行应用测试**

```bash
dotnet run
```

测试步骤：
1. 打开应用，确保默认显示常用命令
2. 检查每个命令卡片是否在名称右侧显示分组标签
3. 验证分组标签样式与 HandyControl 一致
4. 检查标签与命令名称的间距（8px）

**Step 6: Commit**

```bash
git add MainWindow.xaml
git commit -m "feat: 常用命令使用 Tag 控件展示分组信息"
```

---

## 验证清单

- [ ] 常用命令页面每个命令卡片显示分组标签
- [ ] 分组标签使用 `hc:Tag` 控件，样式与 HandyControl 一致
- [ ] 标签位于命令名称右侧，间距 8px
- [ ] 分组名为空时不显示标签
- [ ] 移除了原有的 `[GroupName]` 文本显示
- [ ] 常用命令排序保持不变

---

## 故障排除

**问题：Tag 控件显示为普通文本**
- 检查命名空间声明：`xmlns:hc="https://handyorg.github.io/handycontrol"`
- 检查 HandyControl 包是否正确安装

**问题：分组标签不显示**
- 确认 `GroupName` 属性在 Command 对象中有值
- 检查 `StringToVisibility` 转换器是否正确工作
- 调试 `GetFrequentlyUsedAsync` 是否返回了 GroupName

**问题：样式不匹配**
- 确认使用的是 `hc:Tag` 而非自定义样式
- Tag 控件默认样式与 HandyControl 主题一致
