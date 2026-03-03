using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace BashCommandManager.Infrastructure.Controls;

public partial class EditableTextBlock : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(EditableTextBlock),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty IsEditingProperty =
        DependencyProperty.Register(nameof(IsEditing), typeof(bool), typeof(EditableTextBlock),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsEditingChanged));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(EditableTextBlock));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(EditableTextBlock));

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

    public object CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    private static void OnIsEditingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"OnIsEditingChanged: Old={e.OldValue}, New={e.NewValue}");
        if (d is EditableTextBlock control && (bool)e.NewValue)
        {
            control._originalText = control.Text;
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
        else
        {
            // 显式更新绑定源，确保 Group.Name 被更新
            var bindingExpression = EditText.GetBindingExpression(TextBox.TextProperty);
            bindingExpression?.UpdateSource();
        }

        IsEditing = false;

        if (save && Command != null && Command.CanExecute(CommandParameter))
        {
            Command.Execute(CommandParameter);
        }
    }
}

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility != Visibility.Visible;
        }
        return false;
    }
}
