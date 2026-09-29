using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;

namespace Moss;

public sealed record QuickActionItem(string Action, string Title, string Description);

public partial class QuickActionsWindow : Window
{
    public event Action<string>? ActionRequested;
    public event Action? CopyRequested;
    public event Action? ReplaceRequested;

    public QuickActionsWindow() => InitializeComponent();

    public void ShowActions(IEnumerable<QuickActionItem> actions, string selection, bool allowReplace)
    {
        ActionList.Children.Clear();
        var excerpt = selection.Trim();
        SelectionText.Text = excerpt.Length > 220 ? excerpt[..220].TrimEnd() + "…" : excerpt;
        HeaderText.Text = "Choose a private writing action";
        foreach (var item in actions)
        {
            var content = new StackPanel { Margin = new Thickness(0) };
            content.Children.Add(new TextBlock { Text = item.Title, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Text") });
            content.Children.Add(new TextBlock { Text = item.Description, FontSize = 10, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 3, 0, 0) });
            var button = new Button
            {
                Tag = item.Action,
                Content = content,
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                Padding = new Thickness(11, 9, 11, 9),
                Margin = new Thickness(0, 0, 0, 6)
            };
            button.Click += (_, _) =>
            {
                if (button.Tag is string action) ActionRequested?.Invoke(action);
            };
            ActionList.Children.Add(button);
        }
        MenuView.Visibility = Visibility.Visible;
        ResultView.Visibility = Visibility.Collapsed;
        Height = double.NaN;
    }

    public void SetBusy(string action)
    {
        MenuView.Visibility = Visibility.Collapsed;
        ResultView.Visibility = Visibility.Visible;
        ResultTitle.Text = action;
        OriginalText.Text = "Working with your selected text on this laptop…";
        ResultText.Text = "";
        StatusText.Text = "MossAI is ready offline. This may take a moment.";
        CopyButton.IsEnabled = false;
        ReplaceButton.IsEnabled = false;
        ReplaceButton.Visibility = Visibility.Collapsed;
    }

    public void ShowResult(string action, string original, string result, string status, bool allowReplace)
    {
        MenuView.Visibility = Visibility.Collapsed;
        ResultView.Visibility = Visibility.Visible;
        ResultTitle.Text = action;
        OriginalText.Text = original;
        ResultText.Text = result.Length == 0 ? "No suggestion is available right now. You can try again or close this panel." : result;
        StatusText.Text = status;
        CopyButton.IsEnabled = result.Length > 0;
        ReplaceButton.IsEnabled = result.Length > 0 && allowReplace;
        ReplaceButton.Visibility = allowReplace ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.Visibility = allowReplace ? Visibility.Visible : Visibility.Visible;
        HeaderText.Text = "Review before using";
        Height = double.NaN;
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyRequested?.Invoke();
    private void Replace_Click(object sender, RoutedEventArgs e) => ReplaceRequested?.Invoke();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Popup_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
    }
}
