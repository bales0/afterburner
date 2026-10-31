using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AfterburnerStudio.Models;

namespace AfterburnerStudio.Controls;

public sealed class SocketDiagram : Control
{
    public static readonly StyledProperty<ChipProfile?> ChipProperty =
        AvaloniaProperty.Register<SocketDiagram, ChipProfile?>(nameof(Chip));
    public ChipProfile? Chip { get => GetValue(ChipProperty); set => SetValue(ChipProperty, value); }
    static SocketDiagram() => AffectsRender<SocketDiagram>(ChipProperty);

    public override void Render(DrawingContext c)
    {
        base.Render(c);
        if (Bounds.Width < 1 || Bounds.Height < 1) return;
        double scale = Math.Min(Bounds.Width / 840, Bounds.Height / 420);
        using var transform = c.PushTransform(Matrix.CreateScale(scale, scale));
        static IBrush B(string color) => Brush.Parse(color);
        void Rect(double x, double y, double w, double h, string color) =>
            c.DrawRectangle(B(color), null, new Rect(x, y, w, h), 4, 4);
        void Text(string text, double x, double y, int size = 17, string color = "#17304A") =>
            c.DrawText(new FormattedText(text, CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight,
                new Typeface("Arial"), size, B(color)), new Point(x, y));

        Rect(0, 0, 840, 420, "#F4F8FC");
        Text("Afterburner 3.x — top view, USB on the left", 20, 12, 21);
        Text("USB", 20, 164, 17);
        Rect(87, 76, 660, 267, "#158B96");
        Rect(110, 100, 610, 219, "#D2EEEE");
        c.DrawLine(new Pen(B("#17304A"), 8), new Point(734, 122), new Point(801, 80));
        Rect(784, 61, 36, 33, "#158B96");
        Text("LEVER", 738, 39, 16);
        const double x0 = 135, step = 50;
        for (int column = 0; column < 12; column++)
        {
            double x = x0 + column * step;
            Rect(x - 8, 123, 16, 30, "#DAA739");
            Rect(x - 8, 268, 16, 30, "#DAA739");
            Text(SocketLayout.TopContact(column).ToString(), x - 8, 99, 14);
            Text(SocketLayout.BottomContact(column).ToString(), x - 8, 300, 14);
        }
        var chip = Chip;
        if (chip is null) return;
        if (chip.DipPins is not (20 or 24))
        {
            Text("Adapter / JTAG: orientation depends on the PCB.", 140, 182, 19);
            Text("Use the adapter schematic for its pin connections.", 20, 364, 17, "#B45309");
            return;
        }
        int first = SocketLayout.FirstOccupiedColumn(chip.DipPins);
        double left = x0 + first * step - 18;
        Rect(left, 163, x0 + 11 * step - left + 18, 95, "#19252C");
        // Notch right, pin 1 upper right: matches PCB pad at (2500,850 mil).
        c.DrawEllipse(B("#D2EEEE"), null, new Point(x0 + 11 * step + 18, 210), 10, 13);
        c.DrawEllipse(B("#E34B52"), null, new Point(x0 + 11 * step - 4, 182), 5, 5);
        Text(chip.Name, left + 50, 198, 24, "White");
        for (int column = first; column < 12; column++)
        {
            double x = x0 + column * step;
            Rect(x - 6, 153, 12, 10, "#9BAEB8");
            Rect(x - 6, 258, 12, 10, "#9BAEB8");
            Text(SocketLayout.ChipPin(chip.DipPins, column, true).ToString()!, x - 7, 165, 13, "White");
            Text(SocketLayout.ChipPin(chip.DipPins, column, false).ToString()!, x - 7, 236, 13, "White");
        }
        Text("PIN 1", 723, 166, 17, "#B42318");
        Text(chip.DipPins == 20 ? "DIP20: ZIF 11, 12, 13, 14 are unused at the left." : "DIP24: all 24 contacts are used.", 20, 359);
        Text("Outer numbers: ZIF contacts. Inner numbers: device pins.", 20, 389, 15);
    }
}

