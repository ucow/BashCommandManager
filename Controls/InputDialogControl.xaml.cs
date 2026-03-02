using HandyControl.Controls;
using HandyControl.Tools.Extension;
using System;
using System.Windows;
using System.Windows.Controls;

namespace BashCommandManager.Controls;

public partial class InputDialogControl : UserControl, IDialogResultable<string>
{
    public InputDialogControl()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => TitleTextBlock.Text;
        set => TitleTextBlock.Text = value;
    }

    public string Prompt
    {
        get => (string)GetValue(PromptProperty);
        set => SetValue(PromptProperty, value);
    }

    public static readonly DependencyProperty PromptProperty =
        DependencyProperty.Register(nameof(Prompt), typeof(string), typeof(InputDialogControl), new PropertyMetadata(string.Empty, OnPromptChanged));

    private static void OnPromptChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InputDialogControl control)
        {
            control.TitleTextBlock.Text = e.NewValue?.ToString() ?? string.Empty;
        }
    }

    public string InputText
    {
        get => InputTextBox.Text;
        set => InputTextBox.Text = value;
    }

    public string Result { get; set; } = string.Empty;

    public Action CloseAction { get; set; } = () => { };

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        Result = InputTextBox.Text;
        CloseAction?.Invoke();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = string.Empty;
        CloseAction?.Invoke();
    }
}
