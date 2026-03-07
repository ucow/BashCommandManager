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
    }
}
