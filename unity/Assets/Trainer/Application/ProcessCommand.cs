using WeldingTrainer.Domain;

namespace WeldingTrainer.Application
{
    public enum ProcessCommandType
    {
        EmergencyStop, AbortSession, FinishSession, FinishAttempt, Pause, ResetEmergencyStop,
        Disarm, DisconnectClamp, ConnectClamp, SelectNozzle, Arm, Resume
    }

    public readonly struct ProcessCommand
    {
        public readonly ProcessCommandType Type;
        public readonly double TimestampSeconds;
        public readonly long SubmissionSequence;
        public readonly string Value;
        public ProcessCommand(ProcessCommandType type, double timestamp, long sequence, string value = null)
        { Type = type; TimestampSeconds = timestamp; SubmissionSequence = sequence; Value = value; }

        public int Priority => Type switch
        {
            ProcessCommandType.EmergencyStop => 0,
            ProcessCommandType.AbortSession => 1,
            ProcessCommandType.FinishSession => 2,
            ProcessCommandType.FinishAttempt => 3,
            ProcessCommandType.Pause => 4,
            ProcessCommandType.ResetEmergencyStop => 5,
            ProcessCommandType.Disarm => 6,
            ProcessCommandType.DisconnectClamp => 7,
            ProcessCommandType.ConnectClamp => 8,
            ProcessCommandType.SelectNozzle => 9,
            ProcessCommandType.Arm => 10,
            ProcessCommandType.Resume => 11,
            _ => 100
        };
    }
}
