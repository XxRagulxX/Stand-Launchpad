using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Launchpad;

public partial class MainWindow : Window
{
    private readonly LaunchpadSettings _settings;
    private readonly InjectorRunner _injector;
    private readonly ObservableCollection<DllEntry> _dlls;
    private readonly DispatcherTimer _autoInjectTimer;

    private int  _gtaPid;
    private bool _autoInjectArmed = true;

    public MainWindow()
    {
        InitializeComponent();

        _settings = LaunchpadSettings.Load();
        _dlls     = new ObservableCollection<DllEntry>(_settings.Dlls);
        DllList.ItemsSource = _dlls;

        var launchers = GameLauncher.AvailableLaunchers;
        LauncherType.ItemsSource  = launchers;
        LauncherType.SelectedItem = launchers.FirstOrDefault(l => l.Id == _settings.GameLauncher) ?? launchers[0];

        AutoInjectCheckBox.IsChecked   = _settings.AutoInject;
        AutoInjectDelaySeconds.Value   = _settings.AutoInjectDelaySeconds;
        AdvancedPanel.IsVisible        = _settings.Advanced;

        _autoInjectTimer      = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _autoInjectTimer.Tick += AutoInjectTimer_Tick;

        _injector              = new InjectorRunner();
        _injector.GameStarted += OnGameStarted;
        _injector.GameStopped += OnGameStopped;
        _injector.StartWatching();

        Closing += (_, _) => { _injector.StopWatching(); SaveSettings(); };
    }

    // ── Game state ────────────────────────────────────────────────────────────

    private void OnGameStarted(int pid)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _gtaPid = pid;
            ToggleInjectOrLaunch(gameRunning: true);

            if (AutoInjectCheckBox.IsChecked == true && _autoInjectArmed)
            {
                var delay = (int)(AutoInjectDelaySeconds.Value ?? 0);
                if (_settings.Advanced && delay > 0)
                {
                    InfoText.Text            = "Automatically injecting in a few seconds...";
                    _autoInjectTimer.Interval = TimeSpan.FromSeconds(delay);
                    _autoInjectTimer.Start();
                }
                else
                {
                    _ = InjectAsync();
                }
            }
            else
            {
                InfoText.Text = "Ready to inject.";
            }
        });
    }

    private void OnGameStopped()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _gtaPid = 0;
            _autoInjectTimer.Stop();
            ToggleInjectOrLaunch(gameRunning: false);
            InfoText.Text = "Ready to inject; just start the game.";

            _autoInjectArmed = false;
            DispatcherTimer.RunOnce(() => _autoInjectArmed = true, TimeSpan.FromSeconds(3));
        });
    }

    private void AutoInjectTimer_Tick(object? sender, EventArgs e)
    {
        _autoInjectTimer.Stop();
        _ = InjectAsync();
    }

    private void ToggleInjectOrLaunch(bool gameRunning)
    {
        InjectBtn.IsVisible  = gameRunning;
        LaunchRow.IsVisible  = !gameRunning;
    }

    // ── Inject ────────────────────────────────────────────────────────────────

    private async void InjectBtn_Click(object? sender, RoutedEventArgs e) => await InjectAsync();

    private async Task InjectAsync()
    {
        if (_gtaPid == 0) return;

        _autoInjectTimer.Stop();
        InjectBtn.IsEnabled = false;
        InfoText.Text       = "Injecting...";

        var dlls     = _dlls.Where(d => d.Checked).Select(d => d.Path).ToList();
        string temp  = Path.Combine(Path.GetTempPath(), "LaunchpadInjector");
        int injected = await _injector.InjectAsync(_gtaPid, dlls, line => Console.WriteLine(line));

        InfoText.Text       = $"Injected {injected}/{dlls.Count} DLLs.";
        InjectBtn.IsEnabled = true;
    }

    // ── DLL list ──────────────────────────────────────────────────────────────

    private async void AddBtn_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title            = "Select a DLL to inject",
            AllowMultiple    = true,
            FileTypeFilter   = new[] { new FilePickerFileType("DLL files") { Patterns = new[] { "*.dll" } } },
        });

        foreach (var file in files)
            _dlls.Add(new DllEntry { Path = file.Path.LocalPath, Checked = true });

        SaveSettings();
    }

    private void RemoveBtn_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var item in DllList.SelectedItems!.Cast<DllEntry>().ToList())
            _dlls.Remove(item);
        SaveSettings();
    }

    private void UpBtn_Click(object? sender, RoutedEventArgs e)   => MoveSelected(-1);
    private void DownBtn_Click(object? sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int direction)
    {
        if (DllList.SelectedItems!.Count != 1) return;
        int index    = _dlls.IndexOf((DllEntry)DllList.SelectedItems[0]!);
        int newIndex = index + direction;
        if (newIndex < 0 || newIndex >= _dlls.Count) return;
        _dlls.Move(index, newIndex);
        DllList.SelectedIndex = newIndex;
        SaveSettings();
    }

    // ── Delay ─────────────────────────────────────────────────────────────────

    private void DelayUpBtn_Click(object? sender, RoutedEventArgs e)
    {
        if ((AutoInjectDelaySeconds.Value ?? 0) < 60)
            AutoInjectDelaySeconds.Value = (AutoInjectDelaySeconds.Value ?? 0) + 1;
        SaveSettings();
    }

    private void DelayDownBtn_Click(object? sender, RoutedEventArgs e)
    {
        if ((AutoInjectDelaySeconds.Value ?? 0) > 0)
            AutoInjectDelaySeconds.Value = (AutoInjectDelaySeconds.Value ?? 0) - 1;
        SaveSettings();
    }

    // ── Launch ────────────────────────────────────────────────────────────────

    private async void LaunchBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (LauncherType.SelectedItem is not LauncherOption option) return;
        InfoText.Text  = "Launching...";
        var error      = await _injector.LaunchAsync(option.Id);
        InfoText.Text  = error ?? "Launched.";
    }

    // ── Advanced panel ────────────────────────────────────────────────────────

    private void AdvancedBtn_Click(object? sender, RoutedEventArgs e)
    {
        AdvancedPanel.IsVisible = !AdvancedPanel.IsVisible;
        _settings.Advanced      = AdvancedPanel.IsVisible;
        SaveSettings();
    }

    // ── Open Stand Folder ─────────────────────────────────────────────────────

    private void OpenStandFolderBtn_Click(object? sender, RoutedEventArgs e)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StandEnhanced");
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    // ── Settings persistence ──────────────────────────────────────────────────

    private void LauncherType_SelectionChanged(object? sender, SelectionChangedEventArgs e) => SaveSettings();

    private void AutoInjectCheckBox_Changed(object? sender, RoutedEventArgs e)
    {
        if (AutoInjectCheckBox.IsChecked != true)
            _autoInjectTimer.Stop();
        SaveSettings();
    }

    private void SaveSettings()
    {
        _settings.AutoInject             = AutoInjectCheckBox.IsChecked == true;
        _settings.AutoInjectDelaySeconds = (int)(AutoInjectDelaySeconds.Value ?? 0);
        if (LauncherType.SelectedItem is LauncherOption option)
            _settings.GameLauncher = option.Id;
        _settings.Dlls = _dlls.ToList();
        _settings.Save();
    }
}
