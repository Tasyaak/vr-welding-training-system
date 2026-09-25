using System;

namespace WeldingTrainer.Domain
{
    [Serializable]
    public sealed class FusionAttemptSummary
    {
        public int schemaVersion = 1;
        public string sessionId, attemptId, seamId, contentHash, profileHash;
        public bool completed, interrupted, recordingComplete;
        public double durationSeconds, activeSeconds, blockedTriggerSeconds, invalidSeconds;
        public double attemptedMetres, acceptableMetres, missedMetres, seamMetres;
        public double positionSampleSeconds, speedSampleSeconds, travelSampleSeconds, workSampleSeconds;
        public double meanPositionMm, maximumPositionMm, speedInRangePercent, meanSpeedMmPerSecond;
        public double meanTravelDegrees, meanWorkDegrees, reverseSeconds;
    }

    // Adapted from #72: elapsed-time weighting, with independent validity denominators.
    // Length comes exclusively from the retained coverage union, never frame counts.
    public sealed class FusionAttemptStatistics
    {
        readonly FusionAttemptSummary summary = new();
        double previous = double.NaN, positionSum, speedSum, speedGood, travelSum, workSum;
        bool previousOn;
        public void Add(double time, PathMetrics path, FusionResult fusion, bool requested, double maxGap)
        {
            if (!double.IsFinite(time) || (!double.IsNaN(previous) && time <= previous))
                throw new ArgumentException("Statistics require increasing finite time.");
            double dt = double.IsNaN(previous) ? 0 : time - previous;
            previous = time;
            summary.durationSeconds += dt;
            bool valid = path != null && path.Valid && dt <= maxGap;
            if (!valid)
                summary.invalidSeconds += dt;
            if (requested && !fusion.OutputOn)
                summary.blockedTriggerSeconds += dt;
            if (valid && previousOn && fusion.OutputOn)
            {
                summary.activeSeconds += dt;
                summary.positionSampleSeconds += dt;
                positionSum += path.TotalErrorMetres * 1000 * dt;
                summary.maximumPositionMm = Math.Max(summary.maximumPositionMm, path.TotalErrorMetres * 1000);
                if (path.SpeedValid)
                {
                    summary.speedSampleSeconds += dt;
                    speedSum += Math.Abs(path.FilteredSpeedMps) * 1000 * dt;
                    if (path.SpeedClass == SpeedClass.SpeedCorrect)
                        speedGood += dt;
                }

                if (path.TravelAngleValid)
                {
                    summary.travelSampleSeconds += dt;
                    travelSum += Math.Abs(path.TravelAngleRadians) * 180 / Math.PI * dt;
                }

                if (path.WorkAngleValid)
                {
                    summary.workSampleSeconds += dt;
                    workSum += Math.Abs(path.WorkAngleRadians) * 180 / Math.PI * dt;
                }

                if ((path.Flags & MotionFlags.Reverse) != 0)
                    summary.reverseSeconds += dt;
            }

            previousOn = valid && fusion.OutputOn;
            summary.seamMetres = fusion.Coverage.SeamLengthMetres;
            summary.attemptedMetres = fusion.Coverage.AttemptedLengthMetres;
            summary.acceptableMetres = fusion.Coverage.AcceptableLengthMetres;
            summary.missedMetres = summary.seamMetres - summary.attemptedMetres;
            summary.completed = fusion.Complete;
        }

        public FusionAttemptSummary Finish(bool interrupted)
        {
            summary.interrupted = interrupted;
            summary.completed &= !interrupted;
            summary.meanPositionMm = Mean(positionSum, summary.positionSampleSeconds);
            summary.meanSpeedMmPerSecond = Mean(speedSum, summary.speedSampleSeconds);
            summary.speedInRangePercent = Mean(speedGood * 100, summary.speedSampleSeconds);
            summary.meanTravelDegrees = Mean(travelSum, summary.travelSampleSeconds);
            summary.meanWorkDegrees = Mean(workSum, summary.workSampleSeconds);
            return summary;
        }

        static double Mean(double sum, double seconds) => seconds > 0 ? sum / seconds : 0;
    }
}
