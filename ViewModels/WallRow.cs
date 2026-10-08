using FenCalc2.Models;

namespace FenCalc2.ViewModels;

/// <summary>ListBox/selector row wrapping a WallAssembly so renames and flag changes
/// notify the UI and persist immediately (write-through, no Apply button).</summary>
public class WallRow : ViewModelBase
{
    private readonly WallComplianceViewModel _owner;
    public WallAssembly Data { get; }

    public WallRow(WallComplianceViewModel owner, WallAssembly data)
    {
        _owner = owner;
        Data = data;
    }

    public string Name
    {
        get => Data.Name;
        set
        {
            if (Data.Name == value || string.IsNullOrWhiteSpace(value)) return;
            Data.Name = value;
            _owner.PersistWall(this);
            OnPropertyChanged();
        }
    }

    public bool Category1
    {
        get => Data.Category1;
        set
        {
            if (Data.Category1 == value) return;
            Data.Category1 = value;
            _owner.PersistWall(this);
            OnPropertyChanged();
        }
    }

    public string AttachmentMode
    {
        get => Data.AttachmentMode;
        set
        {
            if (value is null || Data.AttachmentMode == value) return;
            Data.AttachmentMode = value;
            _owner.PersistWall(this);
            OnPropertyChanged();
        }
    }

    public override string ToString() => Data.Name;
}
