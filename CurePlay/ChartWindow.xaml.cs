using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CurePlay.Services;

namespace CurePlay;

public partial class ChartWindow : Window
{
    private readonly HistoryStore _store;
    private readonly string _metric;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private int _minutes = 5;
    public ChartWindow(HistoryStore store, string metric)
    {
        InitializeComponent();
        _store = store; _metric = metric;
        ChartTitle.Text = $"{metric}  /  LIVE HISTORY";
        ChartNote.Text = metric == "PING" ? "ICMP до выбранного узла · пропуски запросов не равны доказанному обрыву интернета." :
            "История последних 15 минут, показания зависят от доступных датчиков.";
        _timer.Tick += (_, _) => Draw();
        Loaded += (_, _) => { Draw(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Period_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button b && int.TryParse(b.Tag?.ToString(), out var n)) _minutes = n;
        Draw();
    }
    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Draw();
    private void Draw()
    {
        if (ChartCanvas is null) return;
        ChartCanvas.Children.Clear();
        var width = ChartCanvas.ActualWidth; var height = ChartCanvas.ActualHeight;
        if (width < 70 || height < 70) return;
        var left = 43d; var top = 12d; var plotWidth = Math.Max(10, width - 55); var plotHeight = Math.Max(10, height - 41);
        var max = _metric == "PING" ? 100d : 100d;
        var samples = _store.Get(_metric, TimeSpan.FromMinutes(_minutes));
        if (_metric == "PING" && samples.Length > 0) max = Math.Max(50, Math.Ceiling(samples.Max(x => x.Value) / 25) * 25);
        for (var i = 0; i <= 4; i++)
        {
            var y = top + i * plotHeight / 4;
            ChartCanvas.Children.Add(new Line { X1 = left, X2 = left + plotWidth, Y1 = y, Y2 = y,
                Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 67, 73)), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 6 } });
            var label = new TextBlock { Text = $"{max * (4 - i) / 4:0}", Foreground = System.Windows.Media.Brushes.LightGray, FontSize = 10 };
            Canvas.SetLeft(label, 5); Canvas.SetTop(label, y - 7); ChartCanvas.Children.Add(label);
        }
        if (samples.Length == 0)
        {
            var text = new TextBlock { Text = "Ожидание измерений...", Foreground = System.Windows.Media.Brushes.LightGray, FontSize = 15 };
            Canvas.SetLeft(text, left + 18); Canvas.SetTop(text, top + plotHeight / 2 - 10); ChartCanvas.Children.Add(text);
            LatestValueText.Text = "—"; return;
        }
        var start = DateTime.Now - TimeSpan.FromMinutes(_minutes);
        var span = TimeSpan.FromMinutes(_minutes).TotalSeconds;
        var points = new PointCollection();
        foreach (var item in samples)
        {
            var px = left + Math.Clamp((item.Time - start).TotalSeconds / span, 0, 1) * plotWidth;
            var py = top + (1 - Math.Clamp(item.Value / max, 0, 1)) * plotHeight;
            points.Add(new System.Windows.Point(px, py));
        }
        ChartCanvas.Children.Add(new Polyline { Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(157, 250, 138)),
            StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round, Points = points });
        LatestValueText.Text = _metric == "PING" && samples[^1].Value < 1
            ? "<1 ms" : $"{samples[^1].Value:0.#}{(_metric == "PING" ? " ms" : " %")}";
    }
}
