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

        // 设置 ContextMenu 的 DataContext
        GroupTree.ContextMenuOpening += (s, e) =>
        {
            if (GroupTree.ContextMenu != null)
            {
                GroupTree.ContextMenu.DataContext = DataContext;
            }
        };
    }

    private void GroupTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm && e.NewValue is Core.Models.Group group)
        {
            vm.GroupTreeViewModel.SelectedGroup = group;
        }
    }
}
