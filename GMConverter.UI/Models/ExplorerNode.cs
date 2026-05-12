using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GMConverter.Explorer;

namespace GMConverter.UI.Models;

public sealed partial class ExplorerNode(string name, ExplorerNodeKind kind = ExplorerNodeKind.Folder) : ObservableObject
{
    private ExplorerNodeKind kind = kind;

    [ObservableProperty]
    private bool isExpanded;

    public string Name { get; } = name;

    public ObservableCollection<ExplorerNode> Children { get; } = [];

    internal ExplorerFileEntry? Entry { get; set; }

    public string Badge => Entry?.InputFormat.ToUpperInvariant() ?? Kind switch
    {
        ExplorerNodeKind.Archive => "PAK",
        _ => "DIR"
    };

    public bool IsModel => Entry is not null;

    public ExplorerNodeKind Kind
    {
        get => kind;
        private set
        {
            if (SetProperty(ref kind, value))
            {
                OnPropertyChanged(nameof(Badge));
            }
        }
    }

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
}

public enum ExplorerNodeKind
{
    Folder,
    Archive,
    Model
}
