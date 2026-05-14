using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GMConverter.Explorer;

namespace GMConverter.UI.Models;

public sealed partial class ExplorerNode(string name, ExplorerNodeKind kind = ExplorerNodeKind.Folder) : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    public string Name { get; } = name;

    public ObservableCollection<ExplorerNode> Children { get; } = [];

    internal ExplorerFileEntry? Entry { get; set; }

    public string Badge => Entry?.InputFormat.ToUpperInvariant() ?? Kind switch
    {
        ExplorerNodeKind.Archive => GetArchiveBadge(Name),
        _ => "DIR"
    };

    public bool IsModel => Entry is not null;

    public ExplorerNodeKind Kind
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(Badge));
            }
        }
    } = kind;

    public void MarkAsArchive()
    {
        if (!IsModel)
        {
            Kind = ExplorerNodeKind.Archive;
        }
    }

    internal void MarkAsModel(ExplorerFileEntry fileEntry)
    {
        Entry = fileEntry;
        Kind = ExplorerNodeKind.Model;
        OnPropertyChanged(nameof(Badge));
        OnPropertyChanged(nameof(IsModel));
    }

    public override string ToString()
    {
        return Name;
    }

    private static string GetArchiveBadge(string name)
    {
        var extension = Path.GetExtension(name);
        return string.IsNullOrWhiteSpace(extension)
            ? "PKG"
            : extension.TrimStart('.').ToUpperInvariant();
    }
}

public enum ExplorerNodeKind
{
    Folder,
    Archive,
    Model
}
