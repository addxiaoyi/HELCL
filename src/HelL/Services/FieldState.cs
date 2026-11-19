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
        "Error", typeof(string), typeof(FieldState), new FrameworkPropertyMetadata("", (field, args) =>
            field.SetValue(HasErrorKey, !string.IsNullOrEmpty((string)args.NewValue))));

    public static string GetError(DependencyObject field) => (string)field.GetValue(ErrorProperty);
    public static void SetError(DependencyObject field, string message) => field.SetValue(ErrorProperty, message);

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
        if (field is TextBox input) input.TextChanged += (_, _) => SetError(field, "");
        if (field is ComboBox combo) combo.SelectionChanged += (_, _) => SetError(field, "");
        return text;
    }
}

public sealed class FieldValidationException : Exception
{
    public UIElement Field { get; }

    public FieldValidationException(UIElement field, string message) : base(message) => Field = field;
}
