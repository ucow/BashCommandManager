using BashCommandManager.ViewModels;
using System.Windows;

namespace BashCommandManager.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = ((App)Application.Current).ServiceProvider.GetRequiredService<SettingsViewModel>();
    }
}
