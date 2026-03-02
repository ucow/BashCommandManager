# 界面文本中文化实施计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 将 MainWindow.xaml 中的所有英文界面文本改为中文

**Architecture:** 直接修改 XAML 文件中的文本属性，保持 UTF-8 编码避免乱码

**Tech Stack:** WPF, XAML, .NET 8

---

## 任务 1: 修改窗口标题和工具栏按钮

**Files:**
- Modify: `MainWindow.xaml:6` - 窗口标题
- Modify: `MainWindow.xaml:19` - 导入命令按钮
- Modify: `MainWindow.xaml:25` - 搜索框占位符
- Modify: `MainWindow.xaml:27` - 搜索按钮

**Step 1: 修改窗口标题**

将第6行：
```xml
Title="Batch Command Manager"
```
改为：
```xml
Title="批处理命令管理器"
```

**Step 2: 修改导入命令按钮**

将第19行：
```xml
<Button Content="Import Command"
```
改为：
```xml
<Button Content="导入命令"
```

**Step 3: 修改搜索框占位符**

将第25行：
```xml
hc:InfoElement.Placeholder="Search commands..."
```
改为：
```xml
hc:InfoElement.Placeholder="搜索命令..."
```

**Step 4: 修改搜索按钮**

将第27行：
```xml
<Button Content="Search"
```
改为：
```xml
<Button Content="搜索"
```

**Step 5: 构建验证**

运行：
```bash
dotnet build
```
预期：成功生成，0个错误

**Step 6: Commit**

```bash
git add MainWindow.xaml
git commit -m "feat: translate toolbar text to Chinese"
```

---

## 任务 2: 修改右键菜单文本

**Files:**
- Modify: `MainWindow.xaml:53` - 新建子分组菜单
- Modify: `MainWindow.xaml:56` - 重命名菜单
- Modify: `MainWindow.xaml:60` - 删除菜单

**Step 1: 修改新建子分组菜单**

将第53行：
```xml
<MenuItem Header="New Subgroup"
```
改为：
```xml
<MenuItem Header="新建子分组"
```

**Step 2: 修改重命名菜单**

将第56行：
```xml
<MenuItem Header="Rename"
```
改为：
```xml
<MenuItem Header="重命名"
```

**Step 3: 修改删除菜单**

将第60行：
```xml
<MenuItem Header="Delete"
```
改为：
```xml
<MenuItem Header="删除"
```

**Step 4: 构建验证**

运行：
```bash
dotnet build
```
预期：成功生成，0个错误

**Step 5: Commit**

```bash
git add MainWindow.xaml
git commit -m "feat: translate context menu text to Chinese"
```

---

## 任务 3: 修改命令卡片按钮文本

**Files:**
- Modify: `MainWindow.xaml:96` - 运行按钮
- Modify: `MainWindow.xaml:100` - 管理员运行按钮
- Modify: `MainWindow.xaml:104` - 删除按钮

**Step 1: 修改运行按钮**

将第96行：
```xml
<Button Content="Run"
```
改为：
```xml
<Button Content="运行"
```

**Step 2: 修改管理员运行按钮**

将第100行：
```xml
<Button Content="Run as Admin"
```
改为：
```xml
<Button Content="管理员运行"
```

**Step 3: 修改删除按钮**

将第104行：
```xml
<Button Content="Delete"
```
改为：
```xml
<Button Content="删除"
```

**Step 4: 最终构建验证**

运行：
```bash
dotnet build
```
预期：成功生成，0个错误

**Step 5: Commit**

```bash
git add MainWindow.xaml
git commit -m "feat: translate command card buttons to Chinese"
```

---

## 完成标准

- [x] MainWindow.xaml 中所有英文文本已改为中文
- [x] 项目能正常编译无错误
- [x] 共修改10处文本
