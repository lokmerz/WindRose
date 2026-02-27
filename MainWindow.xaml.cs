using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Text.RegularExpressions;

namespace WindRose;

public partial class mainWindow : Window
{

    private static readonly Regex _regex = new Regex("[^0-9.,]+");

    private readonly List<WindSector> _sectors =
    [
        new("N", 337.5, 22.5, Color.FromRgb(142, 199, 255)),
        new("NE", 22.5, 67.5, Color.FromRgb(72, 149, 239)),
        new("E", 67.5, 112.5, Color.FromRgb(120, 200, 255)),
        new("SE", 112.5, 157.5, Color.FromRgb(255, 170, 82)),
        new("S", 157.5, 202.5, Color.FromRgb(255, 120, 70)),
        new("SW", 202.5, 247.5, Color.FromRgb(255, 98, 84)),
        new("W", 247.5, 292.5, Color.FromRgb(92, 140, 220)),
        new("NW", 292.5, 337.5, Color.FromRgb(100, 170, 245))
    ];

    private Dictionary<string, LanguagePack> _languages = new();
    private string _currentLanguage = "hr";
    private string _currentTheme = "light";
    private AppSettings _settings = new();
    private readonly Queue<double> _historyAngles = new();
    private double? _displayAngle;
    private DispatcherTimer? _needleAnimationTimer;


    private void LoadSettings()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "settings.json");

        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<AppSettings>(json);
            if (parsed != null)
                _settings = parsed;
        }

        _currentLanguage = _settings.DefaultLanguage;
    }

    private void SaveSettings()
    {
        _settings.DefaultLanguage = _currentLanguage;

        var options = new JsonSerializerOptions { WriteIndented = true };
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "settings.json");

        File.WriteAllText(path, JsonSerializer.Serialize(_settings, options));
    }

    public sealed class LanguagesRoot
    {
        public string DefaultLanguage { get; set; } = "hr";
        public Dictionary<string, LanguagePack> Languages { get; set; } = new();
    }

    public mainWindow()
    {
        InitializeComponent();

        LoadSettings();   // prvo učitaj spremljeni jezik
        LoadLanguages();  // onda učitaj languages.json

        BuildLanguageMenu();
        ConfigureThemeMenuChecks();
        ApplyTheme();
        ApplyLanguage(refreshSelection: true);
        DrawRose();
    }

    private LanguagePack CurrentLanguagePack => _languages.TryGetValue(_currentLanguage, out var pack)
        ? pack
        : _languages.Values.First();

    public string T(string key)
    {
        if (CurrentLanguagePack.Strings.TryGetValue(key, out var value))
        {
            return value;
        }

        return key;
    }

    private void LoadLanguages()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "languages.json");
        if (!File.Exists(path))
        {
            path = System.IO.Path.Combine(Environment.CurrentDirectory, "RuzaVjetrovaWpf", "languages.json");
        }

        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, LanguagePack>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (parsed is not null && parsed.Count > 0)
            {
                _languages = parsed;
            }
        }

        if (_languages.Count == 0)
        {
            throw new InvalidOperationException("languages.json is missing or invalid.");
        }

        if (!_languages.ContainsKey(_currentLanguage))
        {
            _currentLanguage = _languages.Keys.First();
        }
    }

    private void BuildLanguageMenu()
    {
        LanguageMenuItem.Items.Clear();

        foreach (var (code, pack) in _languages)
        {
            var item = new MenuItem
            {
                Header = string.IsNullOrWhiteSpace(pack.DisplayName) ? code : pack.DisplayName,
                Tag = code,
                IsCheckable = true
            };

            item.Click += LanguageMenuItem_Click;
            LanguageMenuItem.Items.Add(item);
        }

        ConfigureLanguageMenuChecks();
    }

    private void ApplyLanguage(bool refreshSelection)
    {
        Title = T("title");
        FileMenuItem.Header = T("file");
        ExitMenuItem.Header = T("exit");
        LanguageMenuItem.Header = T("language");
        ThemesMenuItem.Header = T("themes");
        LightThemeMenuItem.Header = T("lightTheme");
        DarkThemeMenuItem.Header = T("darkTheme");
        AboutMenuItem.Header = T("about");

        InputLabel.Text = T("input");
        ShowButton.Content = T("show");
        WindLabelText.Text = T("selectWind");

        var selection = refreshSelection ? null : WindComboBox.SelectedValue?.ToString();
        var items = new List<WindItem> { new(string.Empty, string.Empty) };
        items.AddRange(_sectors.Select(s => new WindItem(s.Direction, CurrentLanguagePack.WindNames.GetValueOrDefault(s.Direction, s.Direction))));

        WindComboBox.ItemsSource = items;
        WindComboBox.DisplayMemberPath = nameof(WindItem.DisplayName);
        WindComboBox.SelectedValuePath = nameof(WindItem.Direction);
        WindComboBox.SelectedValue = string.IsNullOrWhiteSpace(selection) ? string.Empty : selection;

        UpdateResultTextFromCurrentInput();
        DrawRose(_displayAngle ?? ParseAngleOrNull());
    }

    private void ApplyTheme()
    {
        var appResources = Application.Current.Resources;
        var dictionaries = appResources.MergedDictionaries;

        for (var i = dictionaries.Count - 1; i >= 0; i--)
        {
            var source = dictionaries[i].Source?.OriginalString ?? string.Empty;
            if (source.Contains("Themes/LightTheme.xaml", StringComparison.OrdinalIgnoreCase) ||
                source.Contains("Themes/DarkTheme.xaml", StringComparison.OrdinalIgnoreCase))
            {
                dictionaries.RemoveAt(i);
            }
        }

        var themeUri = _currentTheme == "dark"
            ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
            : new Uri("Themes/LightTheme.xaml", UriKind.Relative);

        dictionaries.Add(new ResourceDictionary { Source = themeUri });
    }

    private void DrawRose(double? angle = null)
    {
        RoseCanvas.Children.Clear();

        const double centerX = 270;
        const double centerY = 270;
        const double radius = 230;

        DrawCompassBase(centerX, centerY, radius);

        foreach (var sector in _sectors)
        {
            var path = BuildSectorPath(centerX, centerY, radius * 0.88, sector.Low, sector.High);
            path.Fill = new SolidColorBrush(sector.Color) { Opacity = 0.28 };
            path.Stroke = _currentTheme == "dark" ? new SolidColorBrush(Color.FromRgb(190, 190, 190)) : new SolidColorBrush(Color.FromRgb(70, 70, 70));
            path.StrokeThickness = 0.7;
            RoseCanvas.Children.Add(path);
        }

        DrawTicks(centerX, centerY, radius * 0.95);
        DrawDirectionLabels(centerX, centerY, radius * 0.90);
        DrawHistoryTrail(centerX, centerY, radius * 0.88);
        DrawGlassOverlay(centerX, centerY, radius * 0.98);

        if (angle.HasValue)
        {
            DrawNeedle(centerX, centerY, radius * 0.86, angle.Value);
        }
    }

    private void DrawCompassBase(double cx, double cy, double radius)
    {
        var outer = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Stroke = _currentTheme == "dark" ? Brushes.Gainsboro : Brushes.DimGray,
            StrokeThickness = 3,
            Fill = _currentTheme == "dark"
                ? new RadialGradientBrush(Color.FromRgb(38, 38, 44), Color.FromRgb(18, 18, 22))
                : new RadialGradientBrush(Color.FromRgb(250, 250, 252), Color.FromRgb(220, 225, 235))
        };
        Canvas.SetLeft(outer, cx - radius);
        Canvas.SetTop(outer, cy - radius);
        RoseCanvas.Children.Add(outer);

        var bezel = new Ellipse
        {
            Width = radius * 2.06,
            Height = radius * 2.06,
            Stroke = _currentTheme == "dark" ? new SolidColorBrush(Color.FromRgb(170, 170, 178)) : new SolidColorBrush(Color.FromRgb(95, 98, 108)),
            StrokeThickness = 8,
            Fill = Brushes.Transparent,
            Opacity = 0.95
        };
        Canvas.SetLeft(bezel, cx - (radius * 1.03));
        Canvas.SetTop(bezel, cy - (radius * 1.03));
        RoseCanvas.Children.Add(bezel);

        foreach (var ringScale in new[] { 0.90, 0.72, 0.50 })
        {
            var ringRadius = radius * ringScale;
            var ring = new Ellipse
            {
                Width = ringRadius * 2,
                Height = ringRadius * 2,
                Stroke = _currentTheme == "dark" ? new SolidColorBrush(Color.FromRgb(115, 115, 125)) : new SolidColorBrush(Color.FromRgb(155, 155, 165)),
                StrokeThickness = ringScale > 0.85 ? 1.4 : 1.0,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(ring, cx - ringRadius);
            Canvas.SetTop(ring, cy - ringRadius);
            RoseCanvas.Children.Add(ring);
        }
    }

    private void DrawTicks(double cx, double cy, double radius)
    {
        for (var deg = 0; deg < 360; deg += 10)
        {
            var isCardinal = deg % 90 == 0;
            var isMajor = deg % 30 == 0;
            var len = isCardinal ? 18 : (isMajor ? 12 : 7);
            var thickness = isCardinal ? 2.2 : (isMajor ? 1.5 : 1.0);

            var p1 = ToPoint(cx, cy, radius, deg);
            var p2 = ToPoint(cx, cy, radius - len, deg);

            RoseCanvas.Children.Add(new Line
            {
                X1 = p1.X,
                Y1 = p1.Y,
                X2 = p2.X,
                Y2 = p2.Y,
                Stroke = _currentTheme == "dark" ? Brushes.Gainsboro : Brushes.Black,
                StrokeThickness = thickness,
                Opacity = isCardinal ? 1 : 0.8
            });
        }
    }

    private void DrawDirectionLabels(double cx, double cy, double radius)
    {
        foreach (var sector in _sectors)
        {
            var labelAngle = MidAngle(sector.Low, sector.High);
            var localizedLabel = CurrentLanguagePack.DirectionLabels.GetValueOrDefault(sector.Direction, sector.Direction);
            var isCardinal = sector.Direction is "N" or "E" or "S" or "W";
            var labelRadius = isCardinal ? radius - 20 : radius - 15;
            var labelPoint = ToPoint(cx, cy, labelRadius, labelAngle);

            var label = new TextBlock
            {
                Text = localizedLabel,
                Foreground = Foreground,
                FontWeight = isCardinal ? FontWeights.ExtraBold : FontWeights.SemiBold,
                FontSize = isCardinal ? 20 : 14
            };

            Canvas.SetLeft(label, labelPoint.X - (isCardinal ? 6 : 7));
            Canvas.SetTop(label, labelPoint.Y - (isCardinal ? 13 : 10));
            RoseCanvas.Children.Add(label);
        }
    }

    private void DrawHistoryTrail(double cx, double cy, double radius)
    {
        if (_historyAngles.Count == 0)
        {
            return;
        }

        var list = _historyAngles.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var alpha = (byte)(40 + i * 30);
            var color = Color.FromArgb(alpha, 245, 190, 85);
            var a = list[i];
            var p1 = ToPoint(cx, cy, radius, a);
            var p2 = ToPoint(cx, cy, radius * 0.62, a);

            RoseCanvas.Children.Add(new Line
            {
                X1 = p1.X,
                Y1 = p1.Y,
                X2 = p2.X,
                Y2 = p2.Y,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 1.5
            });
        }
    }

    private void DrawGlassOverlay(double cx, double cy, double radius)
    {
        var overlay = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            IsHitTestVisible = false,
            Fill = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(35, 255, 255, 255), 0.0),
                    new GradientStop(Color.FromArgb(8, 255, 255, 255), 0.75),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0)
                }
            }
        };
        Canvas.SetLeft(overlay, cx - radius);
        Canvas.SetTop(overlay, cy - radius);
        RoseCanvas.Children.Add(overlay);
    }

    private void DrawNeedle(double centerX, double centerY, double radius, double angle)
    {
        var northTip = ToPoint(centerX, centerY, radius, angle);
        var southTip = ToPoint(centerX, centerY, radius * 0.55, (angle + 180) % 360);
        var leftWing = ToPoint(centerX, centerY, radius * 0.15, angle - 90);
        var rightWing = ToPoint(centerX, centerY, radius * 0.15, angle + 90);

        var glowNeedle = new Polygon
        {
            Points = new PointCollection { northTip, leftWing, southTip, rightWing },
            Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            Effect = new DropShadowEffect { Color = Colors.White, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.45 }
        };
        RoseCanvas.Children.Add(glowNeedle);

        var northNeedle = new Polygon
        {
            Points = new PointCollection { northTip, leftWing, new Point(centerX, centerY), rightWing },
            Fill = new SolidColorBrush(Color.FromRgb(215, 65, 65)),
            Stroke = Brushes.WhiteSmoke,
            StrokeThickness = 1.1
        };
        RoseCanvas.Children.Add(northNeedle);

        var southNeedle = new Polygon
        {
            Points = new PointCollection { southTip, leftWing, new Point(centerX, centerY), rightWing },
            Fill = _currentTheme == "dark" ? new SolidColorBrush(Color.FromRgb(80, 88, 96)) : new SolidColorBrush(Color.FromRgb(120, 126, 133)),
            Stroke = _currentTheme == "dark" ? Brushes.Gainsboro : Brushes.DimGray,
            StrokeThickness = 1.0
        };
        RoseCanvas.Children.Add(southNeedle);

        var cap = new Ellipse
        {
            Width = 14,
            Height = 14,
            Fill = _currentTheme == "dark" ? Brushes.Black : Brushes.White,
            Stroke = _currentTheme == "dark" ? Brushes.Gainsboro : Brushes.DimGray,
            StrokeThickness = 2
        };
        Canvas.SetLeft(cap, centerX - 7);
        Canvas.SetTop(cap, centerY - 7);
        RoseCanvas.Children.Add(cap);
    }

    private static System.Windows.Shapes.Path BuildSectorPath(double cx, double cy, double radius, double low, double high)
    {
        var start = ToPoint(cx, cy, radius, low);
        var end = ToPoint(cx, cy, radius, high);
        var sweep = SweepAngle(low, high);

        var figure = new PathFigure { StartPoint = new Point(cx, cy), IsClosed = true };
        figure.Segments.Add(new LineSegment(start, true));
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, sweep > 180, SweepDirection.Counterclockwise, true));
        figure.Segments.Add(new LineSegment(new Point(cx, cy), true));

        return new System.Windows.Shapes.Path { Data = new PathGeometry([figure]) };
    }

    private static Point ToPoint(double cx, double cy, double r, double angle)
    {
        var radians = (angle - 90) * Math.PI / 180.0;
        return new Point(cx + r * Math.Cos(radians), cy + r * Math.Sin(radians));
    }

    private static double SweepAngle(double low, double high) => high >= low ? high - low : (360 - low) + high;
    private static double MidAngle(double low, double high) => (low + SweepAngle(low, high) / 2) % 360;

    private WindSector? FindSector(double angle) => _sectors.FirstOrDefault(s => s.IsInSector(angle));

    private void ShowButton_Click(object sender, RoutedEventArgs e)
    {
        var angle = ParseAngleOrNull();
        if (angle is null || angle < 0 || angle > 360)
        {
            new CustomAlertWindow(T("error"), T("inputError")).ShowDialog();
            AngleTextBox.ClearValue(TextBox.TextProperty);
            return;
        }

        RenderResult(angle.Value);
    }

    private void RenderResult(double angle)
    {
        var sector = FindSector(angle);
        var directionCode = sector?.Direction;
        var localizedDirection = directionCode is null
            ? T("unknownDirection")
            : CurrentLanguagePack.DirectionLabels.GetValueOrDefault(directionCode, directionCode);
        var windName = directionCode is null
            ? T("unknownWind")
            : CurrentLanguagePack.WindNames.GetValueOrDefault(directionCode, directionCode);

        DirectionResultText.Text = string.Format(T("direction"), localizedDirection, angle);
        WindNameResultText.Text = sector is null
            ? windName
            : string.Format(T("windName"), windName, sector.Low, sector.High);
        ThetaReadoutText.Text = $"θ = {angle:0.0}°";

        TrackHistory(angle);
        AnimateNeedleTo(angle);
    }

    private double? ParseAngleOrNull()
    {
        var input = AngleTextBox.Text.Replace(',', '.');
        return double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var angle) ? angle : null;
    }

    private void AngleTextBox_PreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command == ApplicationCommands.Paste)
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (_regex.IsMatch(text))
                    e.Handled = true;
            }
        }
    }

    private void AngleTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = _regex.IsMatch(e.Text);
    }

    private void UpdateResultTextFromCurrentInput()
    {
        var angle = ParseAngleOrNull();
        if (angle is >= 0 and <= 360)
        {
            RenderResult(angle.Value);
        }
        else
        {
            DirectionResultText.Text = string.Empty;
            WindNameResultText.Text = string.Empty;
            ThetaReadoutText.Text = string.Empty;
        }
    }

    private void WindComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WindComboBox.SelectedValue is not string direction)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(direction))
        {
            AngleTextBox.Text = string.Empty;
            DirectionResultText.Text = string.Empty;
            WindNameResultText.Text = string.Empty;
            ThetaReadoutText.Text = string.Empty;
            _displayAngle = null;
            DrawRose();
            return;
        }

        var sector = _sectors.FirstOrDefault(s => s.Direction == direction);
        if (sector is null)
        {
            return;
        }

        var angle = direction == "N" ? 360 : MidAngle(sector.Low, sector.High);
        AngleTextBox.Text = angle.ToString("0.##", CultureInfo.InvariantCulture);
        RenderResult(angle);
    }

    private void TrackHistory(double angle)
    {
        _historyAngles.Enqueue(angle);
        while (_historyAngles.Count > 5)
        {
            _historyAngles.Dequeue();
        }
    }

    private void AnimateNeedleTo(double targetAngle)
    {
        _needleAnimationTimer?.Stop();

        var startAngle = _displayAngle ?? targetAngle;
        var step = 0;
        const int totalSteps = 12;

        _needleAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(24) };
        _needleAnimationTimer.Tick += (_, _) =>
        {
            step++;
            var t = (double)step / totalSteps;
            var eased = 1 - Math.Pow(1 - t, 3);
            var current = startAngle + (targetAngle - startAngle) * eased;
            _displayAngle = current;
            DrawRose(current);

            if (step >= totalSteps)
            {
                _displayAngle = targetAngle;
                _needleAnimationTimer?.Stop();
            }
        };

        _needleAnimationTimer.Start();
    }

    private void ConfigureLanguageMenuChecks()
    {
        foreach (var item in LanguageMenuItem.Items.OfType<MenuItem>())
        {
            item.IsChecked = string.Equals(item.Tag?.ToString(), _currentLanguage, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void ConfigureThemeMenuChecks()
    {
        LightThemeMenuItem.IsChecked = _currentTheme == "light";
        DarkThemeMenuItem.IsChecked = _currentTheme == "dark";
    }

    private void LanguageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string lang } || !_languages.ContainsKey(lang))
            return;

        _currentLanguage = lang;

        SaveSettings(); // 🔥 OVDJE spremamo izbor

        ConfigureLanguageMenuChecks();
        ApplyLanguage(refreshSelection: false);
    }

    private void LightThemeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _currentTheme = "light";
        ConfigureThemeMenuChecks();
        ApplyTheme();
        DrawRose(_displayAngle ?? ParseAngleOrNull());
    }

    private void DarkThemeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _currentTheme = "dark";
        ConfigureThemeMenuChecks();
        ApplyTheme();
        DrawRose(_displayAngle ?? ParseAngleOrNull());
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        //MessageBox.Show(T("aboutText"), T("about"));
        new CustomAlertWindow(T("about"), T("aboutText")).ShowDialog();

    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public sealed record WindSector(string Direction, double Low, double High, Color Color)
{
    public bool IsInSector(double angle)
    {
        if (Low <= High)
        {
            return angle >= Low && angle < High;
        }

        return angle >= Low || angle < High;
    }
}

public sealed record WindItem(string Direction, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed class LanguagePack
{
    public string DisplayName { get; set; } = string.Empty;
    public Dictionary<string, string> Strings { get; set; } = new();
    public Dictionary<string, string> WindNames { get; set; } = new();
    public Dictionary<string, string> DirectionLabels { get; set; } = new();
}
public sealed class AppSettings
{
    public string DefaultLanguage { get; set; } = "hr";
}
