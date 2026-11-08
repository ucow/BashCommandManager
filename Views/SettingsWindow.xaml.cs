using BashCommandManager.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace BashCommandManager.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = App.ServiceProvider.GetRequiredService<SettingsViewModel>();

        // VM 由根容器解析、实际是单例：每次打开窗口都重新读一份快照，
        // 否则上次「取消」掉的编辑值会残留在界面里，再点保存就被意外持久化。
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
        => (DataContext as SettingsViewModel)?.Reload();
}
