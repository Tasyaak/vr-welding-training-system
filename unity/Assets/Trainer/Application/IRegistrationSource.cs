using UnityEngine;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Application
{
    public enum RegistrationState
    {
        Inactive,
        Scanning,
        Acquired,
        Lost,
    }

    /// Provides the current workpiece registration state from whatever detection method is active.
    public interface IRegistrationSource
    {
        RegistrationState State { get; }
        WorkpieceDefinition WorkpieceDefinition { get; }
        /// Workpiece-local-to-world transform when State == Acquired; undefined otherwise.
        Matrix4x4 LocalToWorld { get; }
        void BeginScanning();
        void StopScanning();
    }
}
