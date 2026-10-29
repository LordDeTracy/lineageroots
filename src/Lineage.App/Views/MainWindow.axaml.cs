using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Lineage.App.ViewModels;

namespace Lineage.App.Views;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is not MainViewModel vm) return;
            vm.StartAutoSave();
            await vm.CheckForUpdatesOnStartAsync();
        };
        ActualThemeVariantChanged += (_, _) => UpdateThemeIcon();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.IsQuickSearchOpen) && vm.IsQuickSearchOpen)
                        Dispatcher.UIThread.Post(() => QuickSearchBox.Focus(), DispatcherPriority.Input);
                };
        };
        UpdateThemeIcon();
    }

    /// <summary>The top bar's theme button shows where it will take you: a sun in dark mode, a moon in light.</summary>
    private void UpdateThemeIcon()
    {
        if (this.TryFindResource(ActualThemeVariant == ThemeVariant.Dark ? "IconSun" : "IconMoon", out var icon) && icon is Avalonia.Media.Geometry g)
            ThemeIcon.Data = g;
    }

    private void OnQuickSearchKey(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        switch (e.Key)
        {
            case Key.Down: vm.MoveQuickSelection(1); break;
            case Key.Up: vm.MoveQuickSelection(-1); break;
            case Key.Enter: vm.RunQuickResultCommand.Execute(null); break;
            case Key.Escape: vm.CloseQuickSearchCommand.Execute(null); break;
            default: return;
        }
        if (vm.SelectedQuickResult != null) QuickList.ScrollIntoView(vm.SelectedQuickResult);
        e.Handled = true;
    }

    private void OnQuickBackdrop(object? sender, PointerPressedEventArgs e) => (DataContext as MainViewModel)?.CloseQuickSearchCommand.Execute(null);

    private void OnQuickResultTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel { SelectedQuickResult: { } r } vm) vm.RunQuickResultCommand.Execute(r);
    }

    /// <summary>Asks about unsaved changes before the window closes.</summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || DataContext is not MainViewModel vm) return;
        e.Cancel = true;
        if (!await vm.CanCloseAsync()) return;
        vm.RememberPlace();
        _closeConfirmed = true;
        Close();
    }

    /// <summary>Closes without the unsaved-changes question (used by screenshots).</summary>
    public void CloseWithoutAsking()
    {
        _closeConfirmed = true;
        Close();
    }

    private void OnNewWindow(object? sender, RoutedEventArgs e) => App.OpenWindow(null);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.N && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            App.OpenWindow(null);
            e.Handled = true;
        }
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    public void ScrollPageTo(double y) => PersonView.ScrollPageTo(y);

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private void OnToggleTheme(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = app.ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        (DataContext as MainViewModel)?.RememberTheme(app.RequestedThemeVariant == ThemeVariant.Dark ? "Dark" : "Light");
    }

    /// <summary>
    /// Renders a window to a PNG and closes it. Used by "LineageRoots --screenshot out.png [file]"
    /// to review the UI in CI or from scripts without a visible desktop session.
    /// </summary>
    public static async Task SaveScreenshotAsync(Window window, string path, int delayMs = 1500)
    {
        await Task.Delay(delayMs);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var scale = window.RenderScaling;
            var size = new PixelSize((int)(window.Bounds.Width * scale), (int)(window.Bounds.Height * scale));
            using var bmp = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
            bmp.Render(window);
            bmp.Save(path);
            if (window is MainWindow main) main.CloseWithoutAsking(); else window.Close();
        });
    }
}
