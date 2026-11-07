using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace MyFlow.App;

/// <summary>Draws MyFlow's menu-bar icon (a small sound-wave) in code, so no image file is needed.</summary>
internal static class TrayIconArt
{
    public static WindowIcon Create()
    {
        const int size = 44; // 22pt @2x
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(144, 144));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            // Black on transparent: macOS recolours "template" icons for light and dark menu bars.
            var brush = Brushes.Black;
            double[] heights = [0.30, 0.62, 1.0, 0.62, 0.30];
            var bar = size / 9.0;
            var gap = bar * 0.85;
            var totalWidth = heights.Length * bar + (heights.Length - 1) * gap;
            var x = (size - totalWidth) / 2;
            foreach (var h in heights)
            {
                var height = (size - 8) * h;
                var rect = new Rect(x, (size - height) / 2, bar, height);
                ctx.DrawRectangle(brush, null, new RoundedRect(rect, bar / 2));
                x += bar + gap;
            }
        }
        var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return new WindowIcon(stream);
    }
}
