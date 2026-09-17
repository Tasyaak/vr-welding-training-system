using WeldingTrainer.Domain;

namespace WeldingTrainer.Application
{
    /// Persists one completed attempt's samples and summary.
    public interface ISessionStorage
    {
        void BeginSession(string sessionId, WorkpieceDefinition workpiece, int seamIndex);
        void RecordSample(ToolSample sample, EvaluationResult result, float elapsedSeconds);
        void FinishAttempt(AttemptSummary summary);
    }
}
