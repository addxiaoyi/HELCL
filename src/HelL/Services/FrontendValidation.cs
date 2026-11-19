using System;
using System.Globalization;
using System.Windows.Controls;

namespace HelL.Services;

internal static class FrontendValidation
{
    public static int Integer(string text, string label, int minimum, int maximum)
    {
        if (!int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            || value < minimum || value > maximum)
            throw new ArgumentException(label + "须为 " + minimum + "–" + maximum + " 之间的整数。");
        return value;
    }
    public static int Integer(TextBox input, string label, int minimum, int maximum)
    {
        try { return Integer(input.Text, label, minimum, maximum); }
        catch (ArgumentException error)
        {
            input.SetResourceReference(Control.BorderBrushProperty, "B.Danger");
            input.ToolTip = error.Message;
            System.Windows.Automation.AutomationProperties.SetHelpText(input, error.Message);
            input.TextChanged -= ClearIntegerError;
            input.TextChanged += ClearIntegerError;
            input.BringIntoView(); input.Focus(); input.SelectAll();
            throw;
        }
    }

    private static void ClearIntegerError(object sender, TextChangedEventArgs args)
    {
        var input = (TextBox)sender;
        input.TextChanged -= ClearIntegerError;
        input.ClearValue(Control.BorderBrushProperty);
        input.ClearValue(System.Windows.FrameworkElement.ToolTipProperty);
        input.ClearValue(System.Windows.Automation.AutomationProperties.HelpTextProperty);
    }
}
