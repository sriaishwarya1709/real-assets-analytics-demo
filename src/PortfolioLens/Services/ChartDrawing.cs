using System.Globalization;

namespace PortfolioLens.Services;

public static class ChartDrawing
{
    public static readonly string[] Colors = ["#55dbc0", "#729df3", "#b29af4", "#edbc74", "#e78fa6", "#7fc9dd", "#b4cf82"];
    public static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    public static string Svg(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    public static string Display(decimal value) => value.ToString("#,0.##", CultureInfo.InvariantCulture);
    public static (decimal Min, decimal Span) Range(ChartResult chart)
    {
        var min = Math.Min(0, chart.Points.Min(p => p.Value));
        var max = Math.Max(0, chart.Points.Max(p => p.Value));
        return (min, max == min ? 1 : max - min);
    }

    public static string PiePath(decimal start, decimal fraction)
    {
        static (double X, double Y) At(decimal turns)
        {
            var angle = (double)turns * Math.PI * 2 - Math.PI / 2;
            return (180 + 140 * Math.Cos(angle), 180 + 140 * Math.Sin(angle));
        }
        var a = At(start);
        var b = At(start + fraction / 2);
        var c = At(start + fraction);
        return $"M 180 180 L {Svg(a.X)} {Svg(a.Y)} A 140 140 0 0 1 {Svg(b.X)} {Svg(b.Y)} A 140 140 0 0 1 {Svg(c.X)} {Svg(c.Y)} Z";
    }
}
