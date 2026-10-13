using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GMConverter.Core.Profiles;
using GMConverter.UI.Models;

namespace GMConverter.UI.ViewModels;

/// <summary>
/// The game the input comes from. Picking an Explorer entry sets it to the game the entry was
/// scanned as; its profile then fills in what the game's conventions reveal, such as animation
/// roles, before export.
/// </summary>
public sealed partial class ConvertViewModel
{
    [ObservableProperty]
    private DisplayOption _selectedImportGame;

    /// <summary>"Unknown", then every game to convert from.</summary>
    public ObservableCollection<DisplayOption> ImportGames { get; } =
    [
        new(string.Empty, "Unknown", string.Empty),
        .. GameCatalog.ImportProfiles.Select(game => new DisplayOption(game.Id, game.DisplayName, game.Engine))
    ];
}
