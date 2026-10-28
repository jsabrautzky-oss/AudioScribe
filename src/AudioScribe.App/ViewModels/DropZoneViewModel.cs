using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using AudioScribe.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AudioScribe.App.ViewModels;

/// <summary>Startbildschirm: Datei (Drag&Drop/Picker), HF-Token (DPAPI), Modell, Min/Max-Sprecher.</summary>
public partial class DropZoneViewModel : ObservableObject
{
    /// <summary>Modell-Eintrag mit Anzeigename (UI) und CLI-/HF-Bezeichner (Value).</summary>
    public sealed record ModelOption(string DisplayName, string Value);
    public const string AllowedExtensions = ".mp3,.wav,.m4a,.aac,.flac,.ogg,.opus,.wma";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private string? _filePath;

    [ObservableProperty]
    private string _fileInfoText = "Noch keine Datei gewählt";

    [ObservableProperty]
    private string _durationText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveTokenBadge))]
    [NotifyPropertyChangedFor(nameof(IsTokenEditPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsTokenBadgeVisible))]
    private string _hfToken = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTokenEditPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsTokenBadgeVisible))]
    [NotifyPropertyChangedFor(nameof(HasActiveTokenBadge))]
    private bool _saveToken = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTokenEditPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsTokenBadgeVisible))]
    [NotifyPropertyChangedFor(nameof(HasActiveTokenBadge))]
    private bool _isStoredTokenSaved;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTokenEditPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsTokenBadgeVisible))]
    [NotifyPropertyChangedFor(nameof(HasActiveTokenBadge))]
    private bool _isEditingToken;

    [ObservableProperty]
    private bool _isHelpOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private string _selectedModel = "large-v3";

    [ObservableProperty]
    private string _minSpeakersText = string.Empty;

    [ObservableProperty]
    private string _maxSpeakersText = string.Empty;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private bool _showGpuArea;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGpuDownloadButton))]
    [NotifyPropertyChangedFor(nameof(ShowGpuSuccess))]
    [NotifyPropertyChangedFor(nameof(UseGpuMode))]
    private bool _hasCudaRuntime;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGpuArea))]
    [NotifyPropertyChangedFor(nameof(ShowGpuPrompt))]
    [NotifyPropertyChangedFor(nameof(ShowGpuDownloadButton))]
    [NotifyPropertyChangedFor(nameof(UseGpuMode))]
    private bool _hasNvidiaGpu;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGpuProgress))]
    [NotifyPropertyChangedFor(nameof(ShowGpuPrompt))]
    [NotifyPropertyChangedFor(nameof(ShowGpuDownloadButton))]
    private bool _isGpuAddonDownloading;

    [ObservableProperty]
    private string _gpuBannerMessage = string.Empty;

    [ObservableProperty]
    private string _gpuStatusMessage = string.Empty;

    [ObservableProperty]
    private double _gpuDownloadProgress;

    [ObservableProperty]
    private string _memoryLabel = string.Empty;

    [ObservableProperty]
    private double _memoryBarPercent;

    [ObservableProperty]
    private bool _memoryWarning;

    [ObservableProperty]
    private string _memoryWarningText = string.Empty;

    private const double LowMemoryThresholdGb = 4.0;

    public bool UseGpuMode => HasNvidiaGpu && HasCudaRuntime;

    public bool ShowGpuProgress => IsGpuAddonDownloading;
    public bool ShowGpuPrompt => HasNvidiaGpu && !HasCudaRuntime && !IsGpuAddonDownloading && _gpuAddon.HasPackageConfigured;
    public bool ShowGpuSuccess => HasCudaRuntime;
    public bool ShowGpuDownloadButton => ShowGpuPrompt;

    public ObservableCollection<ModelOption> AvailableModels { get; } =
    [
        new("large-v3", "large-v3"),
        new("large-v3-turbo", "large-v3-turbo"),
        new("medium", "medium"),
        new("small", "small"),
        new("DE: Tiny (scheidti - Ultraschnell & <1 GB RAM)", "scheidti/whisper-tiny-german-1224-ct2"),
        new("DE: Large V3 Turbo (tnfru - Schnell)", "tnfru/whisper-large-v3-turbo-german-ct2"),
        new("DE: Large V3 (tnfru - Höchste Präzision)", "tnfru/whisper-large-v3-german-ct2"),
    ];

    public bool HasFile => !string.IsNullOrEmpty(FilePath);

    public bool CanStart => HasFile && !string.IsNullOrWhiteSpace(SelectedModel);

    public bool HasActiveTokenBadge => IsStoredTokenSaved && SaveToken && !IsEditingToken &&
                                       !string.IsNullOrWhiteSpace(HfToken);

    public bool IsTokenEditPanelVisible => IsEditingToken || !HasActiveTokenBadge;

    public bool IsTokenBadgeVisible => HasActiveTokenBadge;

    public event Action<string>? OpenFilePickerRequested;

    public event Action? StartRequested;

    private readonly IAppConfigStore _configStore;
    private readonly HardwareDetectorService _hardware;
    private readonly IGpuAddonService _gpuAddon;
    private readonly SystemMemoryService _memory;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer? _memoryTimer;
    private bool _memoryRefreshBusy;
    private CancellationTokenSource? _gpuCts;

    public void NotifyStart()
    {
        if (SaveToken && !string.IsNullOrWhiteSpace(HfToken))
        {
            IsStoredTokenSaved = true;
            IsEditingToken = false;
        }
        StartRequested?.Invoke();
    }

    public void BeginEditToken()
    {
        IsEditingToken = true;
    }

    public void ClearStoredToken()
    {
        HfToken = string.Empty;
        SaveToken = false;
        IsStoredTokenSaved = false;
        IsEditingToken = true;
        _configStore.SaveHfToken(null);
    }

    public void ToggleHelp() => IsHelpOpen = !IsHelpOpen;

    public void DownloadGpuAddon()
    {
        if (IsGpuAddonDownloading || !_gpuAddon.HasPackageConfigured)
            return;

        _gpuCts = new CancellationTokenSource();
        IsGpuAddonDownloading = true;
        GpuDownloadProgress = 0;
        GpuStatusMessage = "CUDA-Paket wird vorbereitet...";

        _ = Task.Run(async () =>
        {
            var result = await _gpuAddon.InstallAsync(_gpuCts.Token);
            _ = _dispatcher.BeginInvoke(() =>
            {
                IsGpuAddonDownloading = false;
                GpuDownloadProgress = result.Success ? 100 : GpuDownloadProgress;
                GpuStatusMessage = result.Message;
                RefreshGpuState();
            });
        });
    }

    private void OnGpuAddonProgress(double percent, string message)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            GpuDownloadProgress = Math.Clamp(percent, 0.0, 100.0);
            if (!string.IsNullOrWhiteSpace(message))
                GpuStatusMessage = message;
        });
    }

    private void RefreshGpuState()
    {
        HasNvidiaGpu = _hardware.HasNvidiaGpu;
        HasCudaRuntime = _hardware.HasCudaRuntime;
        ShowGpuArea = HasNvidiaGpu;

        if (HasCudaRuntime)
            GpuBannerMessage = "GPU-Beschleunigung aktiv (CUDA).";
        else if (HasNvidiaGpu && _gpuAddon.HasPackageConfigured)
            GpuBannerMessage = $"NVIDIA GPU erkannt ({_hardware.NvidiaGpuName}). Für 10x schnellere Transkription GPU-Beschleunigung aktivieren.";
        else if (HasNvidiaGpu)
            GpuBannerMessage = $"NVIDIA GPU erkannt ({_hardware.NvidiaGpuName}), aber kein CUDA-Addon installiert.";
        else
            GpuBannerMessage = string.Empty;

        RefreshMemoryDisplay();
        _ = RefreshMemoryLiveAsync();
    }

    /// <summary>Setzt Label/Balken/Warnung aus dem zuletzt ermittelten RAM/VRAM-Zustand.</summary>
    private void RefreshMemoryDisplay()
    {
        double free;
        double total;
        if (UseGpuMode && _memory.HasVramInfo)
        {
            free = _memory.FreeVramGb;
            total = _memory.TotalVramGb;
            MemoryLabel = $"Freier Grafikspeicher (VRAM): {free:0.#} GB / {total:0.#} GB";
            MemoryWarningText = "Zu wenig freier VRAM für große Modelle.";
        }
        else
        {
            free = _memory.FreeMemoryGb;
            total = _memory.TotalMemoryGb;
            MemoryLabel = $"Freier Arbeitsspeicher (RAM): {free:0.#} GB / {total:0.#} GB";
            MemoryWarningText = "Zu wenig freier RAM für große Modelle.";
        }

        MemoryWarning = free < LowMemoryThresholdGb;
        MemoryBarPercent = total > 0 ? Math.Clamp(free / total * 100.0, 0.0, 100.0) : 0.0;
    }

    private async Task RefreshMemoryLiveAsync()
    {
        if (_memoryRefreshBusy)
            return;
        _memoryRefreshBusy = true;
        try
        {
            await _memory.RefreshAsync();
            _ = _dispatcher.BeginInvoke(RefreshMemoryDisplay);
        }
        finally
        {
            _memoryRefreshBusy = false;
        }
    }

    public DropZoneViewModel(
        IAppConfigStore configStore,
        HardwareDetectorService hardware,
        IGpuAddonService gpuAddon,
        SystemMemoryService memory)
    {
        _configStore = configStore;
        _hardware = hardware;
        _gpuAddon = gpuAddon;
        _memory = memory;
        _dispatcher = Dispatcher.CurrentDispatcher;

        var stored = configStore.GetHfToken();
        HfToken = stored ?? string.Empty;
        SaveToken = stored is not null;
        IsStoredTokenSaved = stored is not null;
        IsEditingToken = stored is null;
        SelectedModel = configStore.Model;

        _gpuAddon.Progress += OnGpuAddonProgress;
        RefreshGpuState();

        _memoryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _memoryTimer.Tick += (_, _) => _ = RefreshMemoryLiveAsync();
        _memoryTimer.Start();
    }

    public void SetFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ValidationMessage = "Datei existiert nicht.";
            return;
        }
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var allowed = AllowedExtensions.Split(',');
        if (!allowed.Contains(ext))
        {
            ValidationMessage = $"Format {ext} wird nicht unterstützt. Erlaubt: {AllowedExtensions}";
            return;
        }

        ValidationMessage = string.Empty;
        FilePath = path;
        DurationText = string.Empty;
        var size = new FileInfo(path).Length;
        FileInfoText = $"{Path.GetFileName(path)} · {size / (1024.0 * 1024.0):0.0} MB";
    }

    public void RequestOpenPicker() => OpenFilePickerRequested?.Invoke(AllowedExtensions);

    public (int? Min, int? Max, string Error) ReadSpeakerBounds()
    {
        if (!TryParseBound(MinSpeakersText, out var mn) || !TryParseBound(MaxSpeakersText, out var mx))
            return (null, null, "Min/Max-Sprecher müssen positive ganze Zahlen sein.");
        if (mn is not null && mx is not null && mn > mx)
            return (null, null, "Min-Sprecher darf nicht größer als Max-Sprecher sein.");
        return (mn, mx, string.Empty);
    }

    private static bool TryParseBound(string? text, out int? value)
    {
        value = null;
        text = (text ?? string.Empty).Trim();
        if (text.Length == 0) return true;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v >= 1)
        {
            value = v;
            return true;
        }
        return false;
    }
}
