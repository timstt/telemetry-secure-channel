using Telemetry.Core;

namespace Telemetry.Predict;

/// <summary>
/// Ознаки з вікном історії: Δaltitude, Δtemp, |vibration|, ковзне відхилення вібрації й висоти,
/// частота зміни mode, critical. Нормалізація рахується на train і зберігається з вагами. Власник: Ваня.
/// </summary>
/// <remarks>
/// Вікно для кадру i — кадри max(0, i − H + 1)…i, тобто лише історія (кадр i і H − 1 попередніх). На початку
/// сеансу вікно коротше, за межі масиву не виходимо. Клас 1 (аномалія) видно за миттєвою вібрацією,
/// клас 2 (перехідний режим) — лише за часовими ознаками: зміни mode у вікні, дрейф висоти й температури.
/// </remarks>
public static class Features
{
    /// <summary>Глибина історії за замовчуванням, кадрів.</summary>
    public const int DefaultHistory = 8;

    /// <summary>Кількість ознак на кадр.</summary>
    public const int Count = 16;

    /// <summary>Матриця ознак [кадр][ознака]; початок сеансу — без виходу за межі.</summary>
    /// <exception cref="ArgumentOutOfRangeException">historyWindow &lt; 2.</exception>
    public static double[][] Extract(Frame[] frames, int historyWindow)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentOutOfRangeException.ThrowIfLessThan(historyWindow, 2);
        var result = new double[frames.Length][];
        Span<bool> modes = stackalloc bool[8];
        for (var i = 0; i < frames.Length; i++)
        {
            var from = Math.Max(0, i - historyWindow + 1);
            var f = frames[i];
            var prev = i > 0 ? frames[i - 1] : f;
            var first = frames[from];
            var shortFrom = Math.Max(0, i - 2);

            double sumV = 0, sumV2 = 0, sumA = 0, sumA2 = 0, sumAbsV = 0, sumDa = 0, sumDa2 = 0, sumT = 0, sumT2 = 0;
            var modeChanges = 0;
            var shortModeChanges = 0;
            var maxAbsV = 0;
            modes.Clear();
            var n = i - from + 1;
            for (var j = from; j <= i; j++)
            {
                var g = frames[j];
                sumV += g.Vibration;
                sumV2 += (double)g.Vibration * g.Vibration;
                sumA += g.AltitudeDm;
                sumA2 += (double)g.AltitudeDm * g.AltitudeDm;
                sumT += g.TempC10;
                sumT2 += (double)g.TempC10 * g.TempC10;
                sumAbsV += Math.Abs((int)g.Vibration);
                maxAbsV = Math.Max(maxAbsV, Math.Abs((int)g.Vibration));
                modes[g.Mode & 7] = true;
                if (j > from)
                {
                    var da = g.AltitudeDm - frames[j - 1].AltitudeDm;
                    sumDa += da;
                    sumDa2 += (double)da * da;
                    if (g.Mode != frames[j - 1].Mode)
                    {
                        modeChanges++;
                        if (j > shortFrom)
                        {
                            shortModeChanges++;
                        }
                    }
                }
            }

            var distinctModes = 0;
            foreach (var m in modes)
            {
                distinctModes += m ? 1 : 0;
            }

            var steps = Math.Max(1, n - 1);
            result[i] =
            [
                f.Vibration,
                Math.Abs((int)f.Vibration),
                f.AltitudeDm - prev.AltitudeDm,
                f.TempC10 - prev.TempC10,
                Std(sumV, sumV2, n),
                Std(sumA, sumA2, n),
                (double)modeChanges / steps,
                f.AltitudeDm - first.AltitudeDm,
                f.TempC10 - first.TempC10,
                sumAbsV / n,
                f.Critical,
                distinctModes,
                Std(sumDa, sumDa2, steps),
                shortModeChanges,
                Std(sumT, sumT2, n),
                maxAbsV,
            ];
        }

        return result;
    }

    private static double Std(double sum, double sumSq, int n)
    {
        if (n <= 1)
        {
            return 0;
        }

        var mean = sum / n;
        return Math.Sqrt(Math.Max(0, (sumSq / n) - (mean * mean)));
    }
}
