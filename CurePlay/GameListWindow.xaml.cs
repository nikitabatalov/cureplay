using System.Windows;
namespace CurePlay;
public partial class GameListWindow : Window
{
    public string[] GameNames { get; private set; } = [];
    public GameListWindow(string names) { InitializeComponent(); NamesBox.Text = names; }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        GameNames = NamesBox.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? x[..^4] : x)
            .Where(x => x.Length > 0 && x.Length <= 100).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
