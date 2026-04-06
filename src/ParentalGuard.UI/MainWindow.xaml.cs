using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ParentalGuard.UI;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const double GraphWidth = 760;
    private const double GraphHeight = 180;

    private readonly DispatcherTimer _timer;
    private readonly ActivityStore _activityStore;
    private readonly DispatcherTimer _settingsSaveTimer;
    private string _currentClockText = string.Empty;
    private string _pageTitle = string.Empty;
    private Geometry _usageLineGeometry = Geometry.Empty;
    private string _blockPopupMessage = string.Empty;
    private Visibility _overviewVisibility = Visibility.Visible;
    private Visibility _usageVisibility = Visibility.Collapsed;
    private Visibility _settingsVisibility = Visibility.Collapsed;
    private Visibility _blockPopupVisibility = Visibility.Collapsed;
    private Brush _overviewButtonBrush = CreateBrush("#173040");
    private Brush _usageButtonBrush = Brushes.Transparent;
    private Brush _settingsButtonBrush = Brushes.Transparent;
    private bool _isRefreshingSettings;

    public MainWindow()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ParentalGuard",
            "usage.db");
        _activityStore = new ActivityStore(appDataPath);

        InitializeComponent();
        DataContext = this;

        AllUsage = [];
        DailyUsageGraph = [];
        BlockRules = new ObservableCollection<BlockRule>(_activityStore.LoadBlockRules());

        UpdatePageCopy();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += OnTimerTick;
        _settingsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _settingsSaveTimer.Tick += OnSettingsSaveTimerTick;

        RefreshVisibleData();
        _timer.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<UsageEntry> AllUsage { get; }

    public ObservableCollection<LineGraphPoint> DailyUsageGraph { get; }

    public ObservableCollection<BlockRule> BlockRules { get; }

    public string CurrentClockText
    {
        get => _currentClockText;
        private set => SetField(ref _currentClockText, value);
    }

    public string PageTitle
    {
        get => _pageTitle;
        private set => SetField(ref _pageTitle, value);
    }

    public Geometry UsageLineGeometry
    {
        get => _usageLineGeometry;
        private set => SetField(ref _usageLineGeometry, value);
    }

    public string BlockPopupMessage
    {
        get => _blockPopupMessage;
        private set => SetField(ref _blockPopupMessage, value);
    }

    public Visibility OverviewVisibility
    {
        get => _overviewVisibility;
        private set => SetField(ref _overviewVisibility, value);
    }

    public Visibility UsageVisibility
    {
        get => _usageVisibility;
        private set => SetField(ref _usageVisibility, value);
    }

    public Visibility SettingsVisibility
    {
        get => _settingsVisibility;
        private set => SetField(ref _settingsVisibility, value);
    }

    public Visibility BlockPopupVisibility
    {
        get => _blockPopupVisibility;
        private set => SetField(ref _blockPopupVisibility, value);
    }

    public Brush OverviewButtonBrush
    {
        get => _overviewButtonBrush;
        private set => SetField(ref _overviewButtonBrush, value);
    }

    public Brush UsageButtonBrush
    {
        get => _usageButtonBrush;
        private set => SetField(ref _usageButtonBrush, value);
    }

    public Brush SettingsButtonBrush
    {
        get => _settingsButtonBrush;
        private set => SetField(ref _settingsButtonBrush, value);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        CurrentClockText = $"{DateTime.Now:ddd dd MMM yyyy  HH:mm:ss}";
        RefreshVisibleData();
    }

    private void UpdatePageCopy()
    {
        if (OverviewVisibility == Visibility.Visible)
        {
            PageTitle = "Overview";
        }
        else if (UsageVisibility == Visibility.Visible)
        {
            PageTitle = "Usage";
        }
        else
        {
            PageTitle = "Settings";
        }
    }

    private void OnOverviewClick(object sender, RoutedEventArgs e)
    {
        OverviewVisibility = Visibility.Visible;
        UsageVisibility = Visibility.Collapsed;
        SettingsVisibility = Visibility.Collapsed;
        OverviewButtonBrush = CreateBrush("#173040");
        UsageButtonBrush = Brushes.Transparent;
        SettingsButtonBrush = Brushes.Transparent;
        UpdatePageCopy();
    }

    private void OnUsageClick(object sender, RoutedEventArgs e)
    {
        OverviewVisibility = Visibility.Collapsed;
        UsageVisibility = Visibility.Visible;
        SettingsVisibility = Visibility.Collapsed;
        OverviewButtonBrush = Brushes.Transparent;
        UsageButtonBrush = CreateBrush("#173040");
        SettingsButtonBrush = Brushes.Transparent;
        UpdatePageCopy();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        OverviewVisibility = Visibility.Collapsed;
        UsageVisibility = Visibility.Collapsed;
        SettingsVisibility = Visibility.Visible;
        OverviewButtonBrush = Brushes.Transparent;
        UsageButtonBrush = Brushes.Transparent;
        SettingsButtonBrush = CreateBrush("#173040");
        UpdatePageCopy();
    }

    private void OnSaveSettingsClick(object sender, RoutedEventArgs e)
    {
        PersistBlockRules(showPopup: true);
    }

    private void OnSettingsSaveTimerTick(object? sender, EventArgs e)
    {
        _settingsSaveTimer.Stop();
        PersistBlockRules(showPopup: false);
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        AnimateIn(HeaderCard, 0);
        AnimateIn(GraphCard, 0.08);
        AnimateIn(UsageCombinedCard, 0.08);
        AnimateIn(SettingsCard, 0.08);
    }

    private void RefreshVisibleData()
    {
        ReplaceCollection(AllUsage, _activityStore.LoadCombinedUsageForDate(DateTime.Today));
        RefreshBlockRules();

        var points = _activityStore.LoadDailyLinePoints(7, GraphWidth, GraphHeight);
        ReplaceCollection(DailyUsageGraph, points);
        UsageLineGeometry = BuildLineGeometry(points);
    }

    private void RefreshBlockRules()
    {
        _isRefreshingSettings = true;
        try
        {
            ReplaceCollection(BlockRules, _activityStore.LoadBlockRules(), AttachRuleHandlers);
        }
        finally
        {
            _isRefreshingSettings = false;
        }
    }

    private void AttachRuleHandlers(BlockRule rule)
    {
        rule.PropertyChanged -= OnBlockRulePropertyChanged;
        rule.PropertyChanged += OnBlockRulePropertyChanged;
    }

    private void OnBlockRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isRefreshingSettings || (e.PropertyName is not nameof(BlockRule.IsEnabled) and not nameof(BlockRule.MaxMinutes)))
        {
            return;
        }

        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private void PersistBlockRules(bool showPopup)
    {
        _activityStore.SaveBlockRules(BlockRules);
        if (showPopup)
        {
            ShowBlockPopup("Block settings saved.");
        }
    }

    private async void ShowBlockPopup(string message)
    {
        BlockPopupMessage = message;
        BlockPopupVisibility = Visibility.Visible;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
        var scaleIn = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var liftIn = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BlockPopupHost.BeginAnimation(OpacityProperty, fadeIn);
        ((ScaleTransform)((TransformGroup)BlockPopupHost.RenderTransform).Children[0]).BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
        ((ScaleTransform)((TransformGroup)BlockPopupHost.RenderTransform).Children[0]).BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
        ((TranslateTransform)((TransformGroup)BlockPopupHost.RenderTransform).Children[1]).BeginAnimation(TranslateTransform.YProperty, liftIn);
        await Task.Delay(2200);

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
        var scaleOut = new DoubleAnimation(1, 0.96, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var dropOut = new DoubleAnimation(0, 10, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (_, _) => BlockPopupVisibility = Visibility.Collapsed;
        BlockPopupHost.BeginAnimation(OpacityProperty, fadeOut);
        ((ScaleTransform)((TransformGroup)BlockPopupHost.RenderTransform).Children[0]).BeginAnimation(ScaleTransform.ScaleXProperty, scaleOut);
        ((ScaleTransform)((TransformGroup)BlockPopupHost.RenderTransform).Children[0]).BeginAnimation(ScaleTransform.ScaleYProperty, scaleOut);
        ((TranslateTransform)((TransformGroup)BlockPopupHost.RenderTransform).Children[1]).BeginAnimation(TranslateTransform.YProperty, dropOut);
    }

    private static Geometry BuildLineGeometry(IReadOnlyList<LineGraphPoint> points)
    {
        if (points.Count == 0)
        {
            return Geometry.Empty;
        }

        var figure = new PathFigure { StartPoint = new Point(points[0].X + 6, points[0].Y + 6) };
        for (var i = 1; i < points.Count; i++)
        {
            figure.Segments.Add(new LineSegment(new Point(points[i].X + 6, points[i].Y + 6), true));
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static void AnimateIn(FrameworkElement element, double beginSeconds)
    {
        var storyboard = new Storyboard();

        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            BeginTime = TimeSpan.FromSeconds(beginSeconds),
            Duration = TimeSpan.FromMilliseconds(380)
        };

        var slide = new DoubleAnimation
        {
            From = 18,
            To = 0,
            BeginTime = TimeSpan.FromSeconds(beginSeconds),
            Duration = TimeSpan.FromMilliseconds(420),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(fade, element);
        Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
        Storyboard.SetTarget(slide, element);
        Storyboard.SetTargetProperty(slide, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

        storyboard.Children.Add(fade);
        storyboard.Children.Add(slide);
        storyboard.Begin();
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items, Action<T>? onItemAdded = null)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
            onItemAdded?.Invoke(item);
        }
    }

    private static SolidColorBrush CreateBrush(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
