using System.IO;
using System.Globalization;
using Microsoft.VisualBasic.FileIO;
using CurePlay.Models;

namespace CurePlay.Services;

// Analyzes measured present-to-present frame times from a PresentMon-like CSV.
// It does NOT claim to capture frames itself or detect stutters using CPU load.
public static class FrameAnalysisService
{
    public static FrameAnalysis ReadCsv(string filename)
    {
        using var parser = new TextFieldParser(filename);
        parser.SetDelimiters(",", ";", "\t");
        parser.HasFieldsEnclosedInQuotes = true;
        var headers = parser.ReadFields() ?? throw new InvalidDataException("В файле отсутствует строка заголовков.");
        var candidates = new[] { "MsBetweenPresents", "msBetweenPresents", "Frametime", "FrameTimeMs", "FrameTime (ms)" };
        var index = Array.FindIndex(headers, h => candidates.Contains(h.Trim(), StringComparer.OrdinalIgnoreCase));
        if (index < 0) throw new InvalidDataException("Не найдена колонка MsBetweenPresents или FrameTimeMs. Экспортируй CSV с временем между кадрами в мс.");
        var frames = new List<double>();
        while (!parser.EndOfData && frames.Count < 200_000)
        {
            var values = parser.ReadFields();
            if (values is null || values.Length <= index) continue;
            if (double.TryParse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) && ms is > 0 and < 10000)
                frames.Add(ms);
        }
        if (frames.Count < 100) throw new InvalidDataException("Для анализа нужно хотя бы 100 корректных измерений времени кадра.");
        frames.Sort();
        var median = frames[frames.Count / 2];
        var p99 = frames[(int)Math.Floor((frames.Count - 1) * .99)];
        var threshold = Math.Max(50, median * 2);
        var stutters = frames.Count(ms => ms >= threshold);
        var slow = frames.Skip((int)Math.Floor(frames.Count * .99)).ToArray();
        // FPS based on average slowest 1% frametimes (an approximation of "1% low").
        return new(frames.Count, stutters, median, p99, 1000d / slow.Average(), headers[index]);
    }
}
