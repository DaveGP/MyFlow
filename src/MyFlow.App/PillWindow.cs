using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MyFlow.Core;

namespace MyFlow.App;

/// <summary>
/// The small floating capsule at the bottom of the screen that shows what MyFlow is doing.
/// It never takes keyboard focus, so text still goes to the app you were typing in.
/// </summary>
public sealed class PillWindow : Window
{
    private readonly Border _dot = new() { Width = 10, Height = 10, CornerRadius = new CornerRadius(5) };
    private readonly TextBlock _label = new()
    {
        Foreground = Brushes.White,
        FontSize = 13,
        FontWeight = FontWeight.Medium,
        VerticalAlignment = VerticalAlignment.Center,
        MaxWidth = 420,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly DispatcherTimer _pulse = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DispatcherTimer _hideTimer = new();
    private double _phase;

    private static readonly IBrush Recording = new SolidColorBrush(Color.Parse("#FF453A"));
    private static readonly IBrush Editing = new SolidColorBrush(Color.Parse("#FF9F0A"));
    private static readonly IBrush Working = new SolidColorBrush(Color.Parse("#64A0FF"));
    private static readonly IBrush Problem = new SolidColorBrush(Color.Parse("#FFD60A"));

    public PillWindow()
    {
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowActivated = false;   // never steal focus from the app being dictated into
        ShowInTaskbar = false;
        CanResize = false;
        Focusable = false;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Title = "MyFlow";

        Content = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#E61C1C1E")),
            BorderBrush = new SolidColorBrush(Color.Parse("#33FFFFFF")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14, 8, 16, 8),
            Margin = new Thickness(6),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 9,
                Children = { _dot, _label },
            },
        };

        _pulse.Tick += (_, _) =>
        {
            _phase += 0.15;
            _dot.Opacity = 0.55 + 0.45 * Math.Abs(Math.Sin(_phase));
        };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            HidePill();
        };
    }

    public void ShowStatus(DictationStatus status)
    {
        _hideTimer.Stop();
        if (status.State == DictationState.Idle)
        {
            if (string.IsNullOrEmpty(status.Message))
            {
                HidePill();
            }
            else
            {
                Display(Working, status.Message, pulse: false);
                HideAfter(TimeSpan.FromSeconds(1.5));
            }
            return;
        }

        var brush = status.State == DictationState.Recording
            ? (status.IsEditMode ? Editing : Recording)
            : Working;
        Display(brush, status.Message, pulse: true);
    }

    public void ShowError(string message)
    {
        Display(Problem, message, pulse: false);
        HideAfter(TimeSpan.FromSeconds(5));
    }

    private void Display(IBrush brush, string text, bool pulse)
    {
        _dot.Background = brush;
        _label.Text = text;
        if (pulse) _pulse.Start(); else { _pulse.Stop(); _dot.Opacity = 1; }

        if (!IsVisible) Show();
        Dispatcher.UIThread.Post(Reposition, DispatcherPriority.Background);
    }

    private void Reposition()
    {
        var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
        if (screen == null) return;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var width = (int)(Bounds.Width * scale);
        var height = (int)(Bounds.Height * scale);
        Position = new PixelPoint(area.X + (area.Width - width) / 2, area.Y + area.Height - height - (int)(40 * scale));
    }

    private void HideAfter(TimeSpan delay)
    {
        _hideTimer.Interval = delay;
        _hideTimer.Start();
    }

    private void HidePill()
    {
        _pulse.Stop();
        if (IsVisible) Hide();
    }
}
