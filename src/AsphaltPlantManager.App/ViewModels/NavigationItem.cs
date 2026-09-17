using CommunityToolkit.Mvvm.ComponentModel;

namespace AsphaltPlantManager.App;

public partial class NavigationItem : ObservableObject
{
    public NavigationItem(string title, string icon, string group, bool showGroupHeader)
    {
        Title = title;
        Icon = icon;
        Group = group;
        ShowGroupHeader = showGroupHeader;
    }

    public string Title { get; }
    public string Icon { get; }
    public string Group { get; }
    public bool ShowGroupHeader { get; }

    [ObservableProperty]
    private bool _isActive;
}
