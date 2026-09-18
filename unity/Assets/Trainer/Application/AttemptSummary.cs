using WeldingTrainer.Domain;

namespace WeldingTrainer.Application
{
    /// Immutable record of one completed training attempt.
    public sealed class AttemptSummary
    {
        public string SessionId { get; }
        public string AttemptId { get; }
        public bool Completed { get; }
        public string FinishReason { get; }
        public float Completion { get; }
        public float AverageErrorMetres { get; }
        public float AverageSpeedMetresPerSecond { get; }
        public float QualityInRangePercent { get; }
        public int TrackingInterruptionCount { get; }
        public float InvalidTrackingSeconds { get; }
        public float AttemptElapsedSeconds { get; }
        public float WeldingActiveSeconds { get; }
        public float BlockedTriggerSeconds { get; }
        public WorkpieceDefinition Workpiece { get; }
        public int SeamIndex { get; }

        public AttemptSummary(
            string sessionId, string attemptId, bool completed, string finishReason,
            float completion, AttemptMetrics metrics,
            WorkpieceDefinition workpiece, int seamIndex)
        {
            SessionId = sessionId;
            AttemptId = attemptId;
            Completed = completed;
            FinishReason = finishReason;
            Completion = completion;
            AverageErrorMetres = metrics.AverageErrorMetres;
            AverageSpeedMetresPerSecond = metrics.AverageSpeedMetresPerSecond;
            QualityInRangePercent = metrics.QualityInRangePercent;
            TrackingInterruptionCount = metrics.TrackingInterruptionCount;
            InvalidTrackingSeconds = metrics.InvalidTrackingSeconds;
            AttemptElapsedSeconds = metrics.AttemptElapsedSeconds;
            WeldingActiveSeconds = metrics.WeldingActiveSeconds;
            BlockedTriggerSeconds = metrics.BlockedTriggerSeconds;
            Workpiece = workpiece;
            SeamIndex = seamIndex;
        }
    }
}
