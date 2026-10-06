using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Troop786.Tablet.Services;

namespace Troop786.Tablet.ViewModels;

/// <summary>Looks a 4-digit code up in the cycle bundle cached on the tablet. Returns the scout id, or null.</summary>
public interface ICodeLookup
{
    Task<string?> FindScoutIdAsync(string code);
    bool HasElectionData { get; }
}

/// <summary>Used until the cycle bundle download is built: the tablet has no election data yet.</summary>
public sealed class UnsyncedCodeLookup : ICodeLookup
{
    public bool HasElectionData => false;
    public Task<string?> FindScoutIdAsync(string code) => Task.FromResult<string?>(null);
}

public partial class CodeEntryViewModel : ObservableObject
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan LockoutLength = TimeSpan.FromSeconds(60);

    private readonly ICodeLookup _codes;
    private int _failedAttempts;
    private DateTimeOffset _lockedUntil = DateTimeOffset.MinValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Dot1), nameof(Dot2), nameof(Dot3), nameof(Dot4))]
    private string _digits = "";

    [ObservableProperty] private string _error = "";
    [ObservableProperty] private int _pendingVotes;
    [ObservableProperty] private bool _isOnline = true;

    public string Dot1 => Digits.Length > 0 ? "●" : "";
    public string Dot2 => Digits.Length > 1 ? "●" : "";
    public string Dot3 => Digits.Length > 2 ? "●" : "";
    public string Dot4 => Digits.Length > 3 ? "●" : "";

    public string StatusText => IsOnline ? "Connected" : "Offline - votes will upload later";
    public string QueueText => $"Votes waiting to upload: {PendingVotes}";
    public string VersionText => $"v{AppInfo.Current.VersionString}";

    /// <summary>Raised with the scout id when a valid code is entered. The shell navigates to the ballot.</summary>
    public event Func<string, Task>? CodeAccepted;

    public CodeEntryViewModel(ICodeLookup codes, SyncCoordinator sync)
    {
        _codes = codes;
        sync.StatusChanged += (pending, online) =>
            MainThread.BeginInvokeOnMainThread(() => { PendingVotes = pending; IsOnline = online; });
    }

    partial void OnPendingVotesChanged(int value) => OnPropertyChanged(nameof(QueueText));
    partial void OnIsOnlineChanged(bool value) => OnPropertyChanged(nameof(StatusText));

    [RelayCommand]
    private void PressDigit(string digit)
    {
        if (Digits.Length >= 4) return;
        Error = "";
        Digits += digit;
    }

    [RelayCommand]
    private void Clear()
    {
        Digits = "";
        Error = "";
    }

    [RelayCommand]
    private async Task EnterAsync()
    {
        var now = DateTimeOffset.UtcNow;
        if (now < _lockedUntil)
        {
            Error = $"Too many attempts. Wait {(int)Math.Ceiling((_lockedUntil - now).TotalSeconds)} seconds.";
            Digits = "";
            return;
        }

        if (Digits.Length != 4)
        {
            Error = "Enter all 4 digits.";
            return;
        }

        if (!_codes.HasElectionData)
        {
            Error = "This tablet has no election data yet. Ask an admin to sync it.";
            Digits = "";
            return;
        }

        var scoutId = await _codes.FindScoutIdAsync(Digits);
        Digits = "";

        if (scoutId is null)
        {
            _failedAttempts++;
            if (_failedAttempts >= MaxAttempts)
            {
                _lockedUntil = DateTimeOffset.UtcNow + LockoutLength;
                _failedAttempts = 0;
                Error = "Too many attempts. Please wait a minute and ask a troop leader for help.";
            }
            else
            {
                Error = "Code not recognized. Please try again.";
            }
            return;
        }

        _failedAttempts = 0;
        Error = "";
        if (CodeAccepted is not null)
            await CodeAccepted.Invoke(scoutId);
    }
}
