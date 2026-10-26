using Avalonia.Controls;

namespace Lineage.App.Views.Pages;

/// <summary>The page for the place selected in the Places list, plus place-name clean-up.</summary>
public partial class PlacesPage : UserControl
{
    public PlacesPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not ViewModels.MainViewModel vm) return;
            vm.PropertyChanged += (_, e) =>
            {
                // Picking a place with GPS moves the map to it.
                if (e.PropertyName == nameof(ViewModels.MainViewModel.SelectedPlace) && vm.SelectedPlace?.Entry.Location is { } at)
                {
                    PlacesMap.Center = at;
                    PlacesMap.Zoom = Math.Max(PlacesMap.Zoom, 10);
                }
            };
        };
    }
}
