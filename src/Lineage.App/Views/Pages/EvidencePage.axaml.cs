using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Lineage.App.ViewModels;

namespace Lineage.App.Views.Pages;

public partial class EvidencePage : UserControl
{
    private MainViewModel? _vm;

    public EvidencePage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.PropertyChanged -= OnChanged;
            _vm = DataContext as MainViewModel;
            if (_vm != null) _vm.PropertyChanged += OnChanged;
        };
        AttachedToVisualTree += (_, _) => ToTop();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedEvidence)) ToTop();
    }

    /// <summary>A newly chosen fact opens at its question, not wherever the last one was scrolled to.</summary>
    private void ToTop() => Dispatcher.UIThread.Post(() => Detail.Offset = default, DispatcherPriority.Background);
}
