using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lineage.App.ViewModels;

namespace Lineage.App.Views;

/// <summary>
/// Large photo viewer with tagging. Show with <c>ShowDialog&lt;bool&gt;</c>: true when the user
/// clicked Done or a tagged person's name (then see <see cref="PhotoViewerViewModel.OpenPersonId"/>).
/// Escape stops tagging first, then cancels; the arrow keys move between photos.
/// </summary>
public partial class PhotoViewerWindow : Window
{
    private PhotoViewerViewModel? _vm;

    public PhotoViewerWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        // After the picker has taken its pick from the drop-down, Enter tags the person.
        PersonBox.AddHandler(KeyDownEvent, OnPersonBoxKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        TagList.AddHandler(PointerMovedEvent, OnTagListPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        TagList.PointerExited += (_, _) => { if (_vm != null) _vm.HighlightedTag = null; };
        Closed += (_, _) => _vm?.Dispose();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null)
        {
            _vm.OpenPersonRequested -= OnOpenPerson;
            _vm.PropertyChanged -= OnViewModelChanged;
        }
        _vm = DataContext as PhotoViewerViewModel;
        if (_vm != null)
        {
            _vm.OpenPersonRequested += OnOpenPerson;
            _vm.PropertyChanged += OnViewModelChanged;
        }
    }

    private void OnOpenPerson(long id) => Close(true);

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A box was just drawn: let the user type the name straight away.
        if (e.PropertyName == nameof(PhotoViewerViewModel.PendingRegion) && _vm?.HasPendingRegion == true)
            Dispatcher.UIThread.Post(() => (PersonBox.FindDescendantOfType<TextBox>() as InputElement ?? PersonBox).Focus(),
                                     DispatcherPriority.Background);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm == null) return;
        if (e.Key == Key.Escape)
        {
            if (_vm.IsTagging) _vm.CancelTaggingCommand.Execute(null);
            else Close(false);
            e.Handled = true;
        }
        else if (e.Key is Key.Left or Key.Right && !IsTyping(e.Source))
        {
            var go = e.Key == Key.Left ? _vm.PreviousCommand : _vm.NextCommand;
            if (go.CanExecute(null)) go.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _vm.HasPendingRegion && !InPersonBox(e.Source))
        {
            _vm.ConfirmTagCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnPersonBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _vm == null) return;
        _vm.ConfirmTagCommand.Execute(null);
        e.Handled = true;
    }

    private void OnTagListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_vm == null) return;
        var row = (e.Source as StyledElement)?.DataContext as TagRow;
        if (!ReferenceEquals(row, _vm.HighlightedTag)) _vm.HighlightedTag = row;
    }

    private bool InPersonBox(object? source) => source is Visual v && (v == PersonBox || v.FindAncestorOfType<AutoCompleteBox>() == PersonBox);

    private static bool IsTyping(object? source) =>
        source is TextBox || (source is Visual v && v.FindAncestorOfType<AutoCompleteBox>() != null);

    private void OnDone(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
