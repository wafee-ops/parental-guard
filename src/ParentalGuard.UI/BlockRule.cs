using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ParentalGuard.UI;

public sealed class BlockRule : INotifyPropertyChanged
{
    private bool _isEnabled;
    private int _maxMinutes;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string TargetType { get; init; }

    public required string TargetKey { get; init; }

    public required string DisplayName { get; init; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;
            OnPropertyChanged();
        }
    }

    public int MaxMinutes
    {
        get => _maxMinutes;
        set
        {
            if (_maxMinutes == value)
            {
                return;
            }

            _maxMinutes = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
