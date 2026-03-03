using HandyControl.Controls;
using HandyControl.Tools.Extension;
using System;
using System.Windows;
using System.Windows.Controls;

namespace BashCommandManager.Controls;

public partial class ConfirmDialogControl : UserControl, IDialogResultable<bool>
{
    public ConfirmDialogControl()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => TitleTextBlock.Text;
        set => TitleTextBlock.Text = value;
    }

    public string Message
    {
        get => MessageTextBlock.Text;
        set => MessageTextBlock.Text = value;
    }

    public bool Result { get; set; } = false;

    public Action CloseAction { get; set; } = () => { };

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        Result = true;
        CloseAction?.Invoke();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = false;
        CloseAction?.Invoke();
    }
}
