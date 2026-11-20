using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace HelL.Services;

public static class FieldState
{
    private static readonly DependencyPropertyKey HasErrorKey = DependencyProperty.RegisterAttachedReadOnly(
        "HasError", typeof(bool), typeof(FieldState), new FrameworkPropertyMetadata(false));
    public static readonly DependencyProperty HasErrorProperty = HasErrorKey.DependencyProperty;
    public static bool GetHasError(DependencyObject field) => (bool)field.GetValue(HasErrorProperty);
    public static readonly DependencyProperty ErrorProperty = DependencyProperty.RegisterAttached(
        "Error", typeof(string), typeof(FieldState), new FrameworkPropertyMetadata("", ErrorChanged));

    public static string GetError(DependencyObject field) => (string)field.GetValue(ErrorProperty);
    public static void SetError(DependencyObject field, string message) => field.SetValue(ErrorProperty, message);

    private static void ErrorChanged(DependencyObject field, DependencyPropertyChangedEventArgs args)
    {
        bool hasError = !string.IsNullOrEmpty((string)args.NewValue);
        field.SetValue(HasErrorKey, hasError);
        if (field is TextBox input)
        {
            input.TextChanged -= ClearTextError;
            if (hasError) input.TextChanged += ClearTextError;
        }
        else if (field is PasswordBox password)
        {
            password.PasswordChanged -= ClearPasswordError;
            if (hasError) password.PasswordChanged += ClearPasswordError;
        }
        else if (field is ComboBox combo)
        {
            combo.SelectionChanged -= ClearSelectionError;
            if (hasError) combo.SelectionChanged += ClearSelectionError;
        }
    }

    private static void ClearTextError(object sender, TextChangedEventArgs args) => SetError((DependencyObject)sender, "");
    private static void ClearPasswordError(object sender, RoutedEventArgs args) => SetError((DependencyObject)sender, "");
    private static void ClearSelectionError(object sender, SelectionChangedEventArgs args) => SetError((DependencyObject)sender, "");

    public static TextBlock ErrorText(UIElement field)
    {
        var style = new Style(typeof(TextBlock), Ui.S("T.Small"));
        style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        style.Triggers.Add(new Trigger
        {
            Property = TextBlock.TextProperty,
            Value = "",
            Setters = { new Setter(UIElement.VisibilityProperty, Visibility.Collapsed) }
        });
        var text = new TextBlock { Style = style };
        text.SetResourceReference(TextBlock.ForegroundProperty, "B.Danger");
        text.SetBinding(TextBlock.TextProperty, new Binding { Source = field, Path = new PropertyPath(ErrorProperty) });
        return text;
    }
}

public sealed class FieldValidationException : Exception
{
    public UIElement Field { get; }

    public FieldValidationException(UIElement field, string message) : base(message) => Field = field;
}
