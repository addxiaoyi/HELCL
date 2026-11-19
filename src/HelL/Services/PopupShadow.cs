using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace HelL.Services;

// Keep text out of the effect's offscreen bitmap so it retains normal font rendering.
public sealed class PopupShadow : Decorator
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(PopupShadow),
        new FrameworkPropertyMetadata(new CornerRadius(8), OnCornerChanged));

    private readonly Border _shadow = new()
    {
        CornerRadius = new CornerRadius(8),
        IsHitTestVisible = false,
        Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Direction = 270, Opacity = .14 }
    };
    private bool _separateWindow;

    public PopupShadow()
    {
        _shadow.SetResourceReference(Border.BackgroundProperty, "B.PopupShadow");
        AddVisualChild(_shadow);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var source = PresentationSource.FromVisual(this);
        _separateWindow = source != null && AppServices.Window != null && source != PresentationSource.FromVisual(AppServices.Window);
        App.ThemeVisualsChanged -= SyncMaterial;
        App.ThemeVisualsChanged += SyncMaterial;
        SyncMaterial();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeVisualsChanged -= SyncMaterial;
        OverlayReadability.Cover(this, false);
    }

    private void SyncMaterial()
    {
        bool menu = TemplatedParent is ComboBox or System.Windows.Controls.ContextMenu or MenuItem;
        OverlayReadability.Cover(this, _separateWindow && menu && Glass.PopupTransparency >= 70, dialogs: true);
        _shadow.SetResourceReference(Border.BackgroundProperty,
            _separateWindow && Glass.PopupTransparency == 100 ? "B.PopupInput" : "B.PopupShadow");
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    protected override int VisualChildrenCount => base.VisualChildrenCount + 1;

    protected override Visual GetVisualChild(int index) => index == 0 ? _shadow : base.GetVisualChild(index - 1);

    protected override Size MeasureOverride(Size constraint)
    {
        _shadow.Measure(constraint);
        return base.MeasureOverride(constraint);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        _shadow.Arrange(new Rect(arrangeSize));
        return base.ArrangeOverride(arrangeSize);
    }

    private static void OnCornerChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) =>
        ((PopupShadow)target)._shadow.CornerRadius = (CornerRadius)args.NewValue;
}
