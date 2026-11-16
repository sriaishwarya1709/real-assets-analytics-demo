using System.Globalization;
using System.Text;

namespace PortfolioLens.Services;

// A small vector PDF report using PDF's standard Helvetica font; no browser or native renderer is needed.
public static class PdfReport
{
    public static byte[] Create(ChartResult chart, string question, string mode)
    {
        var pages = new List<string>();
        var cover = new StringBuilder();
        Text(cover, 42, 798, 12, "PORTFOLIO LENS / REAL ASSETS INTELLIGENCE");
        Text(cover, 42, 766, 18, chart.Title);
        Text(cover, 42, 741, 10, $"Mode: {mode} | As of: {chart.AsOf} | Unit: {chart.Unit}");
        Text(cover, 42, 723, 10, mode == "Sample" ? "SYNTHETIC SAMPLE DATA - not connected to Fabric" : "AI-generated analysis - verify against source data");
        if (chart.ChartType == "pie")
        {
            var total = chart.Points.Sum(p => p.Value);
            double angle = -Math.PI / 2;
            for (var i = 0; i < chart.Points.Length; i++)
            {
                var fraction = (double)(chart.Points[i].Value / total);
                var steps = Math.Max(1, (int)Math.Ceiling(fraction * 100));
                SetColor(cover, ChartDrawing.Colors[i % ChartDrawing.Colors.Length]);
                cover.AppendLine("295 500 m");
                for (var step = 0; step <= steps; step++)
                {
                    var theta = angle + fraction * Math.PI * 2 * step / steps;
                    cover.AppendLine($"{N(295 + 145 * Math.Cos(theta))} {N(500 + 145 * Math.Sin(theta))} l");
                }
                cover.AppendLine("h f");
                angle += fraction * Math.PI * 2;
            }
        }
        else
        {
            var (min, span) = ChartDrawing.Range(chart);
            var zero = 260 + (double)(-min / span * 270);
            for (var i = 0; i < chart.Points.Length; i++)
            {
                var p = chart.Points[i];
                var y = 670 - i * 19;
                var end = 260 + (double)((p.Value - min) / span * 270);
                SetColor(cover, ChartDrawing.Colors[i % ChartDrawing.Colors.Length]);
                cover.AppendLine($"{N(Math.Min(zero, end))} {y} {N(Math.Abs(end - zero))} 10 re f");
                Text(cover, 42, y + 1, 8, p.Label.Length > 31 ? p.Label[..28] + "..." : p.Label);
                Text(cover, 535, y + 1, 7, ChartDrawing.Display(p.Value));
            }
        }
        Text(cover, 42, 220, 10, "Full values, source labels and notes follow on the next pages.");
        Text(cover, 42, 202, 9, "Source labels are reported by the model, not independently verified citations.");
        Text(cover, 42, 184, 9, "Not investment advice. PDF standard font replaces unsupported characters with '?'.");
        pages.Add(cover.ToString());
        var details = new StringBuilder();
        var cursor = 795;
        void Line(string text, int size = 10)
        {
            foreach (var line in Wrap(text, 88))
            {
                if (cursor < 55)
                {
                    pages.Add(details.ToString());
                    details.Clear();
                    cursor = 795;
                }
                Text(details, 42, cursor, size, line);
                cursor -= 16;
            }
            cursor -= 5;
        }
        Line(chart.Title, 14);
        Line("Question: " + question);
        Line("Summary: " + chart.Summary);
        Line("Data source labels: " + string.Join(", ", chart.Sources));
        Line("Reporting date: " + chart.AsOf);
        if (!string.IsNullOrEmpty(chart.Warning)) Line("Warning: " + chart.Warning);
        Line("DATA / " + chart.Unit, 12);
        foreach (var p in chart.Points) Line($"{p.Label}: {ChartDrawing.Display(p.Value)}");
        Line($"Generated (UTC): {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}");
        Line($"Mode: {mode}. AI-generated analysis must be verified before use. Not investment advice.");
        pages.Add(details.ToString());
        return Encode(pages);
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ');
        for (var offset = 0; offset < text.Length; offset += width)
            yield return text.Substring(offset, Math.Min(width, text.Length - offset));
    }

    private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static void SetColor(StringBuilder b, string hex) => b.AppendLine(
        $"{N(Convert.ToInt32(hex[1..3], 16) / 255d)} {N(Convert.ToInt32(hex[3..5], 16) / 255d)} {N(Convert.ToInt32(hex[5..7], 16) / 255d)} rg");
    private static void Text(StringBuilder b, int x, int y, int size, string text)
    {
        var safe = new string(text.Select(c => c is >= ' ' and <= '~' ? c : '?').ToArray())
            .Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        b.AppendLine($"0.1 0.15 0.2 rg BT /F1 {size} Tf {x} {y} Td ({safe}) Tj ET");
    }

    private static byte[] Encode(List<string> pages)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Count {pages.Count} /Kids [{string.Join(" ", pages.Select((_, i) => $"{4 + i * 2} 0 R"))}] >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        foreach (var page in pages)
        {
            var contentId = objects.Count + 2;
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentId} 0 R >>");
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(page)} >>\nstream\n{page}endstream");
        }
        using var buffer = new MemoryStream();
        void Write(string text) => buffer.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(buffer.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = buffer.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return buffer.ToArray();
    }
}
