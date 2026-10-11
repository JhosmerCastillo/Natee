using Avalonia.Controls;
using Natee.App.ViewModels;

namespace Natee.App.Views;

public partial class MainWindow : Window
{
    public bool AllowImmediateClose { get; set; }

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!AllowImmediateClose &&
            DataContext is MainWindowViewModel vm &&
            vm.MinimizeToTrayOnClose)
        {
            // Keep Natee running in the system tray when the user closes the dashboard window
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
