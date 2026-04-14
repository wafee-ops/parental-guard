using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace ParentalGuard.UI;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const double GraphWidth = 760;
    private const double GraphHeight = 180;

    private readonly DispatcherTimer _timer;
    private readonly ActivityStore _activityStore;
    private readonly DispatcherTimer _settingsSaveTimer;
    private bool _isAddingApp;
    private string _currentClockText = string.Empty;

    private string _todayTotalText = "0m";
    private string _yesterdayTotalText = "0m";
    private string _lastWeekTotalText = "0m";
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
        AllowedRules = [];
        BlockedRules = [];
        SplitRules();

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

    public ObservableCollection<BlockRule> AllowedRules { get; }

    public ObservableCollection<BlockRule> BlockedRules { get; }

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

    public string TodayTotalText
    {
        get => _todayTotalText;
        private set => SetField(ref _todayTotalText, value);
    }

    public string YesterdayTotalText
    {
        get => _yesterdayTotalText;
        private set => SetField(ref _yesterdayTotalText, value);
    }

    public string LastWeekTotalText
    {
        get => _lastWeekTotalText;
        private set => SetField(ref _lastWeekTotalText, value);
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
        AnimatePageIn(OverviewGrid);
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
        AnimatePageIn(UsageGrid);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        PinInput.Password = string.Empty;
        PinError.Visibility = Visibility.Collapsed;
        PinInstruction.Text = "PIN required to access settings.";
        PinPopupOverlay.Visibility = Visibility.Visible;
        AnimateOverlayIn(PinPopupOverlay);
        PinInput.Focus();
    }

    private void OnPinCancelClick(object sender, RoutedEventArgs e)
    {
        PinPopupOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnPinSubmitClick(object sender, RoutedEventArgs e)
    {
        if (PinInput.Password == "1234")
        {
            PinPopupOverlay.Visibility = Visibility.Collapsed;
            NavigateToSettings();
        }
        else
        {
            PinError.Visibility = Visibility.Visible;
            PinInstruction.Text = "Incorrect PIN. Please try again.";
            PinInput.Password = string.Empty;
            PinInput.Focus();
        }
    }

    private void NavigateToSettings()
    {
        OverviewVisibility = Visibility.Collapsed;
        UsageVisibility = Visibility.Collapsed;
        SettingsVisibility = Visibility.Visible;
        OverviewButtonBrush = Brushes.Transparent;
        UsageButtonBrush = Brushes.Transparent;
        SettingsButtonBrush = CreateBrush("#173040");
        UpdatePageCopy();
        AnimatePageIn(SettingsGrid);
    }

    private void OnAddAppClick(object sender, RoutedEventArgs e)
    {
        _isAddingApp = true;
        PopupTitle.Text = "Add Application";
        PopupInstruction.Text = "Enter the process name (e.g., discord, chrome).";
        PopupInput.Text = string.Empty;
        InputPopupOverlay.Visibility = Visibility.Visible;
        AnimateOverlayIn(InputPopupOverlay);
        PopupInput.Focus();
    }

    private void OnAddWebsiteClick(object sender, RoutedEventArgs e)
    {
        _isAddingApp = false;
        PopupTitle.Text = "Add Website";
        PopupInstruction.Text = "Enter the domain name (e.g., facebook.com).";
        PopupInput.Text = string.Empty;
        InputPopupOverlay.Visibility = Visibility.Visible;
        AnimateOverlayIn(InputPopupOverlay);
        PopupInput.Focus();
    }

    private void OnPopupCancelClick(object sender, RoutedEventArgs e)
    {
        InputPopupOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnDeleteRuleClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not BlockRule rule) return;
        BlockRules.Remove(rule);
        AllowedRules.Remove(rule);
        BlockedRules.Remove(rule);
        _activityStore.DeleteBlockRule(rule.TargetType, rule.TargetKey);
        ShowBlockPopup($"Removed {rule.DisplayName}");
    }

    private void OnMoveToBlockedClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not BlockRule rule) return;
        var idx = BlockRules.IndexOf(rule);
        if (idx < 0) return;
        var newRule = new BlockRule
        {
            TargetType = rule.TargetType,
            TargetKey = rule.TargetKey,
            DisplayName = rule.DisplayName,
            IsEnabled = true,
            MaxMinutes = rule.MaxMinutes,
            ListType = "blocked"
        };
        BlockRules[idx] = newRule;
        _activityStore.SetListType(rule.TargetType, rule.TargetKey, "blocked");
        SplitRules();
        PersistBlockRules(showPopup: false);
        ShowBlockPopup($"Blocked {rule.DisplayName}");
    }

    private void OnMoveToAllowedClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not BlockRule rule) return;
        var idx = BlockRules.IndexOf(rule);
        if (idx < 0) return;
        var newRule = new BlockRule
        {
            TargetType = rule.TargetType,
            TargetKey = rule.TargetKey,
            DisplayName = rule.DisplayName,
            IsEnabled = true,
            MaxMinutes = rule.MaxMinutes,
            ListType = "allowed"
        };
        BlockRules[idx] = newRule;
        _activityStore.SetListType(rule.TargetType, rule.TargetKey, "allowed");
        SplitRules();
        PersistBlockRules(showPopup: false);
        ShowBlockPopup($"Allowed {rule.DisplayName}");
    }

    private void OnPopupAddClick(object sender, RoutedEventArgs e)
    {
        var input = PopupInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        var targetType = _isAddingApp ? "app" : "website";
        var rule = new BlockRule
        {
            TargetType = targetType,
            TargetKey = input,
            DisplayName = input,
            IsEnabled = true,
            MaxMinutes = 60,
            ListType = "allowed"
        };

        BlockRules.Add(rule);
        _activityStore.SetListType(rule.TargetType, rule.TargetKey, "allowed");
        SplitRules();
        PersistBlockRules(showPopup: true);
        InputPopupOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnSaveSettingsClick(object sender, RoutedEventArgs e)

    {
        PersistBlockRules(showPopup: true);
    }

    private void OnResetAllSettingsClick(object sender, RoutedEventArgs e)
    {
        ResetConfirmOverlay.Visibility = Visibility.Visible;
        AnimateOverlayIn(ResetConfirmOverlay);
    }

    private void OnResetCancelClick(object sender, RoutedEventArgs e)
    {
        ResetConfirmOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnResetConfirmClick(object sender, RoutedEventArgs e)
    {
        ResetConfirmOverlay.Visibility = Visibility.Collapsed;
        _activityStore.DeleteAllBlockRules();
        BlockRules.Clear();
        AllowedRules.Clear();
        BlockedRules.Clear();
        _activityStore.SaveBlockRules([]);
        RefreshBlockRules();
        ShowBlockPopup("All settings have been reset to defaults.");
    }

    private void OnSettingsSaveTimerTick(object? sender, EventArgs e)
    {
        _settingsSaveTimer.Stop();
        PersistBlockRules(showPopup: false);
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        EnableBlur(this);
        AnimateIn(HeaderCard, 0.12);
        AnimateIn(GraphCard, 0.2);
        AnimateIn(UsageCombinedCard, 0.2);
        AnimateIn(SettingsCard, 0.2);
        AnimateTopBar();
        AnimateSidebar();
    }

    private void OnWindowStateChanged(object sender, EventArgs e)
    {
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void RefreshVisibleData()
    {
        ReplaceCollection(AllUsage, _activityStore.LoadCombinedUsageForDate(DateTime.Today));
        RefreshBlockRules();
        RefreshOverviewTotals();

        var points = _activityStore.LoadDailyLinePoints(7, GraphWidth, GraphHeight);
        ReplaceCollection(DailyUsageGraph, points);
        UsageLineGeometry = BuildLineGeometry(points);
    }

    private void RefreshOverviewTotals()
    {
        var todaySeconds = _activityStore.GetTotalUsageSeconds(DateTime.Today);
        var yesterdaySeconds = _activityStore.GetTotalUsageSeconds(DateTime.Today.AddDays(-1));
        var weekStart = DateTime.Today.AddDays(-6);
        var weekSeconds = _activityStore.GetTotalUsageSecondsInRange(weekStart, DateTime.Today);

        TodayTotalText = FormatDuration(totalSeconds: todaySeconds);
        YesterdayTotalText = FormatDuration(totalSeconds: yesterdaySeconds);
        LastWeekTotalText = FormatDuration(totalSeconds: weekSeconds);
    }

    private void RefreshBlockRules()
    {
        _isRefreshingSettings = true;
        try
        {
            ReplaceCollection(BlockRules, _activityStore.LoadBlockRules(), AttachRuleHandlers);
            SplitRules();
        }
        finally
        {
            _isRefreshingSettings = false;
        }
    }

    private void SplitRules()
    {
        ReplaceCollection(AllowedRules, BlockRules.Where(r => r.ListType == "allowed"));
        ReplaceCollection(BlockedRules, BlockRules.Where(r => r.ListType == "blocked"));
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

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var scaleIn = new DoubleAnimation(0.88, 1, TimeSpan.FromMilliseconds(400))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }
        };
        var liftIn = new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(400))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 }
        };
        BlockPopupHost.BeginAnimation(OpacityProperty, fadeIn);
        ((ScaleTransform)((TransformGroup)BlockPopupHost.RenderTransform).Children[0]).BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
        ((ScaleTransform)((TransformGroup)BlockPopupHost.RenderTransform).Children[0]).BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
        ((TranslateTransform)((TransformGroup)BlockPopupHost.RenderTransform).Children[1]).BeginAnimation(TranslateTransform.YProperty, liftIn);
        await Task.Delay(2400);

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var scaleOut = new DoubleAnimation(1, 0.94, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var dropOut = new DoubleAnimation(0, 14, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
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
            Duration = TimeSpan.FromMilliseconds(480)
        };

        var slide = new DoubleAnimation
        {
            From = 28,
            To = 0,
            BeginTime = TimeSpan.FromSeconds(beginSeconds),
            Duration = TimeSpan.FromMilliseconds(560),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
        };

        Storyboard.SetTarget(fade, element);
        Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
        Storyboard.SetTarget(slide, element);
        Storyboard.SetTargetProperty(slide, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

        storyboard.Children.Add(fade);
        storyboard.Children.Add(slide);
        storyboard.Begin();
    }

    private void AnimateTopBar()
    {
        var accentIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700))
        {
            BeginTime = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        TopBarAccentLine.BeginAnimation(OpacityProperty, accentIn);

        var shimmerIn = new DoubleAnimation(0, 0.6, TimeSpan.FromMilliseconds(900))
        {
            BeginTime = TimeSpan.FromMilliseconds(100),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        TopBarShimmer.BeginAnimation(OpacityProperty, shimmerIn);

        var logoScaleX = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(550))
        {
            BeginTime = TimeSpan.FromMilliseconds(80),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 }
        };
        var logoScaleY = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(550))
        {
            BeginTime = TimeSpan.FromMilliseconds(80),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 }
        };
        ((ScaleTransform)TopBarLogo.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, logoScaleX);
        ((ScaleTransform)TopBarLogo.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, logoScaleY);

        var glowPulse = new DoubleAnimation(0, 0.55, TimeSpan.FromMilliseconds(1800))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        LogoGlow.BeginAnimation(DropShadowEffect.OpacityProperty, glowPulse);

        var titleFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450))
        {
            BeginTime = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var titleSlide = new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(450))
        {
            BeginTime = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        TopBarTitleText.BeginAnimation(OpacityProperty, titleFade);
        ((TranslateTransform)TopBarTitleText.RenderTransform).BeginAnimation(TranslateTransform.XProperty, titleSlide);

        var dotPulse = new DoubleAnimation(0.8, 2.2, TimeSpan.FromMilliseconds(2200))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        ((ScaleTransform)StatusDotGlow.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, dotPulse);
        ((ScaleTransform)StatusDotGlow.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, dotPulse);

        var dotOpacity = new DoubleAnimation(0.35, 0, TimeSpan.FromMilliseconds(2200))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        StatusDotGlow.BeginAnimation(OpacityProperty, dotOpacity);

        var shimmerBreath = new DoubleAnimation(0.4, 0.7, TimeSpan.FromMilliseconds(3000))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromMilliseconds(900),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        TopBarShimmer.BeginAnimation(OpacityProperty, shimmerBreath);
    }

    private void AnimateSidebar()
    {
        var transform = new TranslateTransform(-220, 0);
        SidebarBorder.RenderTransform = transform;
        SidebarBorder.Opacity = 0;

        var slideIn = new DoubleAnimation(-220, 0, TimeSpan.FromMilliseconds(600))
        {
            BeginTime = TimeSpan.FromMilliseconds(50),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500))
        {
            BeginTime = TimeSpan.FromMilliseconds(50),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        transform.BeginAnimation(TranslateTransform.XProperty, slideIn);
        SidebarBorder.BeginAnimation(OpacityProperty, fadeIn);
    }

    private static void AnimatePageIn(FrameworkElement element)
    {
        element.Opacity = 0;
        var transform = new TranslateTransform(24, 0);
        element.RenderTransform = transform;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var slideIn = new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.15 }
        };
        element.BeginAnimation(OpacityProperty, fadeIn);
        transform.BeginAnimation(TranslateTransform.XProperty, slideIn);
    }

    private static void AnimateOverlayIn(Grid overlay)
    {
        overlay.Opacity = 0;
        var childBorder = FindVisualChild<Border>(overlay);
        if (childBorder != null)
        {
            childBorder.RenderTransformOrigin = new Point(0.5, 0.5);
            childBorder.RenderTransform = new ScaleTransform(0.9, 0.9);
            var dialogScale = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }
            };
            ((ScaleTransform)childBorder.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, dialogScale);
            ((ScaleTransform)childBorder.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, dialogScale);
        }
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        overlay.BeginAnimation(OpacityProperty, fadeIn);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            var descendant = FindVisualChild<T>(child);
            if (descendant != null) return descendant;
        }
        return null;
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

    private static string FormatDuration(int totalSeconds)
    {
        if (totalSeconds < 60)
        {
            return $"{Math.Max(1, totalSeconds)}s";
        }

        var duration = TimeSpan.FromSeconds(totalSeconds);
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes:00}m";
        }

        return $"{duration.Minutes}m";
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static void EnableBlur(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        try
        {
            int value = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
        catch { }

        try
        {
            var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }
        catch { }

        try
        {
            int value = DWMSBT_TRANSIENTWINDOW;
            var hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref value, sizeof(int));
            if (hr == 0) return;
        }
        catch { }

        try
        {
            int value = DWMSBT_MAINWINDOW;
            var hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref value, sizeof(int));
            if (hr == 0) return;
        }
        catch { }

        try
        {
            var accent = new AccentPolicy
            {
                AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2,
                GradientColor = unchecked((int)0x66000000)
            };
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                SizeOfData = Marshal.SizeOf<AccentPolicy>(),
                Data = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>())
            };
            Marshal.StructureToPtr(accent, data.Data, false);
            SetWindowCompositionAttribute(hwnd, ref data);
            Marshal.FreeHGlobal(data.Data);
        }
        catch { }

        try
        {
            var accent = new AccentPolicy
            {
                AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND,
                AccentFlags = 2,
                GradientColor = unchecked((int)0x66000000)
            };
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                SizeOfData = Marshal.SizeOf<AccentPolicy>(),
                Data = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>())
            };
            Marshal.StructureToPtr(accent, data.Data, false);
            SetWindowCompositionAttribute(hwnd, ref data);
            Marshal.FreeHGlobal(data.Data);
        }
        catch { }
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_MAINWINDOW = 2;
    private const int DWMSBT_TRANSIENTWINDOW = 3;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    private enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4
    }
}
