using BashCommandManager.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Tools.Extension;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace BashCommandManager.Controls;

public partial class GroupSelectionDialog : UserControl, IDialogResultable<Group?>
{
    public GroupSelectionDialog()
    {
        InitializeComponent();
        var viewModel = new GroupSelectionDialogViewModel();
        DataContext = viewModel;

        // 同步 ViewModel 的 Result 和 CloseAction 到控件
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(GroupSelectionDialogViewModel.Result))
            {
                Result = viewModel.Result;
            }
        };
        viewModel.CloseAction = () => CloseAction?.Invoke();
    }

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is GroupSelectionDialogViewModel vm)
        {
            vm.SelectedGroup = e.NewValue as Group;
        }
    }

    public Group? Result { get; set; }
    public Action? CloseAction { get; set; }
}

public partial class GroupSelectionDialogViewModel : ObservableObject, IDialogResultable<Group?>
{
    [ObservableProperty]
    private string _title = "选择目标分组";

    [ObservableProperty]
    private ObservableCollection<Group> _groups = new();

    [ObservableProperty]
    private Group? _selectedGroup;

    [ObservableProperty]
    private int? _excludeGroupId;

    public bool CanConfirm => SelectedGroup != null && SelectedGroup.Id != ExcludeGroupId;

    public Group? Result { get; set; }
    public Action? CloseAction { get; set; }

    [RelayCommand]
    private void Confirm()
    {
        if (SelectedGroup != null && SelectedGroup.Id != ExcludeGroupId)
        {
            Result = SelectedGroup;
            CloseAction?.Invoke();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseAction?.Invoke();
    }

    partial void OnSelectedGroupChanged(Group? value)
    {
        OnPropertyChanged(nameof(CanConfirm));
    }
}
