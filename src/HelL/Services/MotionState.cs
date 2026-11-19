using System.ComponentModel;
using System.Windows;

namespace HelL.Services;

public sealed class MotionState : INotifyPropertyChanged
{
    public static MotionState Current { get; } = new MotionState();
    public bool Enabled => Motion.Enabled;
    public event PropertyChangedEventHandler? PropertyChanged;

    private MotionState()
    {
        Motion.EnabledChanged += Refresh;
        SystemParameters.StaticPropertyChanged += (_, _) => Refresh();
    }

    private void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
}
