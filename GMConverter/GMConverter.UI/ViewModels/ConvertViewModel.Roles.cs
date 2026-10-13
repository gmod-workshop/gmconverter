using System.Collections.ObjectModel;
using GMConverter.SDK.Animation;
using GMConverter.UI.Services;

namespace GMConverter.UI.ViewModels;

/// <summary>
/// The animation roles table. It lists the previewed model's animations with the role each was
/// guessed to have, and lets the user override them; overrides are kept by clip name for the
/// current input and sent with every conversion.
/// </summary>
public sealed partial class ConvertViewModel
{
    public ObservableCollection<AnimationRoleViewModel> AnimationRoles { get; } = [];

    public bool HasAnimationRoles => AnimationRoles.Count > 0;

    /// <summary>Lists a preview's animations, keeping the choices already made for clips still present.</summary>
    internal void ApplyPreviewAnimations(IReadOnlyList<PreviewAnimation> animations)
    {
        var previous = AnimationRoles.ToDictionary(row => row.ClipName, row => row.SelectedChoice, StringComparer.OrdinalIgnoreCase);
        AnimationRoles.Clear();
        foreach (var animation in animations)
        {
            var row = new AnimationRoleViewModel(animation.Name, animation.GuessedRole);
            if (previous.TryGetValue(animation.Name, out var choice))
            {
                row.SelectedChoice = choice;
            }
            AnimationRoles.Add(row);
        }

        OnPropertyChanged(nameof(HasAnimationRoles));
    }

    partial void OnInputPathChanged(string value)
    {
        // Overrides name the previous input's clips.
        if (AnimationRoles.Count > 0)
        {
            AnimationRoles.Clear();
            OnPropertyChanged(nameof(HasAnimationRoles));
        }
    }

    private Dictionary<string, AnimationRole?> BuildRoleOverrides()
    {
        return AnimationRoles
            .Where(row => row.IsOverridden)
            .ToDictionary(row => row.ClipName, row => row.OverrideRole, StringComparer.OrdinalIgnoreCase);
    }
}
