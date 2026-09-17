using WeldingTrainer.Domain;

namespace WeldingTrainer.Application
{
    /// Provides a current tool tip sample from whatever tracking source is active.
    public interface IToolTracker
    {
        ToolSample GetSample(float timestampSeconds);
    }
}
