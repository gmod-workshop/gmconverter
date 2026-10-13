using CommunityToolkit.Mvvm.ComponentModel;
using GMConverter.Core.Animation;
using GMConverter.SDK.Animation;

namespace GMConverter.UI.ViewModels;

/// <summary>
/// One animation in the roles table: its guessed role and the choice that overrides it. Choosing
/// an action keeps the guessed stance and direction; choosing "none" clears the role.
/// </summary>
public sealed partial class AnimationRoleViewModel : ObservableObject
{
    public const string GuessedChoice = "guessed";

    [ObservableProperty]
    private string _selectedChoice = GuessedChoice;

    internal AnimationRoleViewModel(string clipName, AnimationRole? guessedRole)
    {
        ClipName = clipName;
        GuessedRole = guessedRole;
    }

    /// <summary>"guessed", "none", then every action.</summary>
    public static IReadOnlyList<string> Choices { get; } = [GuessedChoice, AnimationRoleText.None, .. AnimationRoleText.Actions];

    public string ClipName { get; }

    internal AnimationRole? GuessedRole { get; }

    public string GuessedText => AnimationRoleText.Format(GuessedRole).Replace(",", ", ", StringComparison.Ordinal);

    public bool IsOverridden => SelectedChoice != GuessedChoice;

    /// <summary>The role chosen instead of the guess. Only meaningful when <see cref="IsOverridden"/>.</summary>
    internal AnimationRole? OverrideRole => SelectedChoice == AnimationRoleText.None
        ? null
        : AnimationRoleText.Parse(SelectedChoice) is { } chosen
            ? chosen with { Stance = GuessedRole?.Stance, Direction = GuessedRole?.Direction }
            : null;

    partial void OnSelectedChoiceChanged(string value)
    {
        OnPropertyChanged(nameof(IsOverridden));
    }
}
