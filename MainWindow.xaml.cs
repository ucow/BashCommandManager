using BashCommandManager.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace BashCommandManager;

public partial class MainWindow : Window
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
}
