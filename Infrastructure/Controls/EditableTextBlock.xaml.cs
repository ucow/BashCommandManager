using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BashCommandManager.Infrastructure.Controls;

public partial class EditableTextBlock : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(EditableTextBlock),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsEditingProperty =
        DependencyProperty.Register(nameof(IsEditing), typeof(bool), typeof(EditableTextBlock),
            new PropertyMetadata(false, OnIsEditingChanged));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(EditableTextBlock));

    private string _originalText = string.Empty;

    public EditableTextBlock()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        set => SetValue(IsEditingProperty, value);
    }

    public ICommand Command
    {
        get => (ICommand)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    private static void OnIsEditingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EditableTextBlock control && (bool)e.NewValue)
        {
            control._originalText = control.Text;
        }
    }

    private void DisplayText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            StartEdit();
        }
    }

    private void EditText_Loaded(object sender, RoutedEventArgs e)
    {
        EditText.Focus();
        EditText.SelectAll();
    }

    private void EditText_LostFocus(object sender, RoutedEventArgs e)
    {
        EndEdit(true);
    }

    private void EditText_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                EndEdit(true);
                e.Handled = true;
                break;
            case Key.Escape:
                EndEdit(false);
                e.Handled = true;
                break;
        }
    }

    public void StartEdit()
    {
        _originalText = Text;
        IsEditing = true;
    }

    private void EndEdit(bool save)
    {
        if (!IsEditing) return;

        if (!save)
        {
            Text = _originalText;
        }

        IsEditing = false;

        if (save && Command != null && Command.CanExecute(Text))
        {
            Command.Execute(Text);
        }
    }
}
