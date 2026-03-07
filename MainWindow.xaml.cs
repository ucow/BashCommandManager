using BashCommandManager.ViewModels;
using HandyControl.Controls;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BashCommandManager;

public partial class MainWindow : HandyControl.Controls.Window
{
    private bool _isExiting = false; // 标记是否真正退出

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // 动态加载图标（支持单文件发布）
        LoadIcon();

        Loaded += async (s, e) =>
        {
            await viewModel.GroupTreeViewModel.LoadGroupsAsync();
        };

        // 订阅窗体状态变化事件
        StateChanged += MainWindow_StateChanged;
    }

    /// <summary>
    /// 动态加载窗口和托盘图标
    /// </summary>
    private void LoadIcon()
    {
        try
        {
            // 使用 Pack URI 加载图标（支持单文件发布）
            var iconUri = new Uri("pack://application:,,,/Assets/app.ico");
            Icon = new System.Windows.Media.Imaging.BitmapImage(iconUri);
        }
        catch
        {
            // 图标加载失败不影响应用运行
        }
    }

    private void GroupTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm && e.NewValue is Core.Models.Group group)
        {
            vm.GroupTreeViewModel.SelectedGroup = group;
        }
    }

    private void TreeViewItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is TreeViewItem item && item.ContextMenu != null && DataContext is MainViewModel vm)
        {
            // 检查是否是虚拟节点
            var selectedGroup = vm.GroupTreeViewModel.SelectedGroup;
            bool isVirtual = selectedGroup?.IsVirtual ?? false;

            foreach (var child in item.ContextMenu.Items)
            {
                if (child is MenuItem menuItem)
                {
                    switch (menuItem.Header)
                    {
                        case "新建子分组":
                            menuItem.Command = vm.GroupTreeViewModel.CreateGroupCommand;
                            // 虚拟节点隐藏新建子分组菜单
                            menuItem.Visibility = isVirtual ? Visibility.Collapsed : Visibility.Visible;
                            break;
                        case "重命名":
                            menuItem.Command = vm.GroupTreeViewModel.StartRenameGroupCommand;
                            // 虚拟节点隐藏重命名菜单
                            menuItem.Visibility = isVirtual ? Visibility.Collapsed : Visibility.Visible;
                            break;
                        case "删除":
                            menuItem.Command = vm.GroupTreeViewModel.DeleteGroupCommand;
                            // 虚拟节点隐藏删除菜单
                            menuItem.Visibility = isVirtual ? Visibility.Collapsed : Visibility.Visible;
                            break;
                    }
                }
            }
        }
    }

    private void TrayIcon_MouseDoubleClick(object sender, RoutedEventArgs e)
    {
        ShowWindow();
    }

    private void ShowWindow_Click(object sender, RoutedEventArgs e)
    {
        ShowWindow();
    }

    private void ShowWindow()
    {
        Show(); // 显示窗体
        WindowState = WindowState.Normal; // 恢复窗体状态
        Activate(); // 激活窗体到前台
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _isExiting = true; // 设置退出标志
        Close(); // 调用关闭方法
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide(); // 隐藏窗体（从任务栏消失）
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isExiting)
        {
            // 真正退出时清理托盘图标
            TrayIcon?.Dispose();
            base.OnClosing(e);
            return;
        }

        e.Cancel = true; // 取消关闭操作
        WindowState = WindowState.Minimized; // 触发最小化
        Hide(); // 隐藏窗体
        base.OnClosing(e);
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // 触发搜索命令
            if (DataContext is MainViewModel vm)
            {
                vm.SearchCommand.Execute(null);
            }
        }
    }
}
