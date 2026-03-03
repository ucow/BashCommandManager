using BashCommandManager.ViewModels;
using HandyControl.Controls;
using System.Windows;
using System.Windows.Controls;

namespace BashCommandManager;

public partial class MainWindow : HandyControl.Controls.Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += async (s, e) =>
        {
            await viewModel.GroupTreeViewModel.LoadGroupsAsync();
        };
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
}
