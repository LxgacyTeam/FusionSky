using System;
using System.Diagnostics;
using System.Text;

namespace DynamicSky
{
    internal static class FrameDiagnostics
    {
        internal enum Stage { Apply, CloudMask, CubemapColors, CubemapUpload, LocalProbeRequest, LodAndImpostors, Frame }

        private static readonly double MsPerTick = 1000d / Stopwatch.Frequency;
        private static readonly bool Enabled = Environment.GetEnvironmentVariable("DYNAMIC_SKY_DIAGNOSTICS") == "1";
        private static readonly Stage[] Stages = (Stage[])Enum.GetValues(typeof(Stage));
        private static readonly Stats[] Samples = new Stats[Stages.Length];
        private static double nextReport;

        private struct Stats
        {
            internal int Count;
            internal double Total, Max;
        }

        internal readonly struct Sample : IDisposable
        {
            private readonly Stage stage;
            private readonly long start;

            internal Sample(Stage stage)
            {
                this.stage = stage;
                start = Enabled ? Stopwatch.GetTimestamp() : 0;
            }

            public void Dispose()
            {
                if (start != 0) Add(stage, (Stopwatch.GetTimestamp() - start) * MsPerTick);
            }
        }

        internal static Sample Measure(Stage stage) => new Sample(stage);

        private static void Add(Stage stage, double ms)
        {
            ref var sample = ref Samples[(int)stage];
            sample.Count++;
            sample.Total += ms;
            if (ms > sample.Max) sample.Max = ms;
        }

        internal static void ReportFrame(float elapsedSeconds)
        {
            if (!Enabled) return;
            Add(Stage.Frame, elapsedSeconds * 1000d);
            double now = Stopwatch.GetTimestamp() * MsPerTick;
            if (nextReport == 0) nextReport = now + 10000;
            if (now < nextReport) return;
            nextReport = now + 10000;

            var report = new StringBuilder("Frame timings (ms, avg/max, count):");
            foreach (Stage stage in Stages)
            {
                var sample = Samples[(int)stage];
                if (sample.Count == 0) continue;
                report.AppendFormat(" {0}={1:F2}/{2:F2} ({3});", stage, sample.Total / sample.Count, sample.Max, sample.Count);
            }
            Plugin.Log?.LogInfo(report.ToString());
            Array.Clear(Samples, 0, Samples.Length);
        }
    }
}

