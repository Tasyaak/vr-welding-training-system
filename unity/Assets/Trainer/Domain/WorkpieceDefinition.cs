using System;
using UnityEngine;

namespace WeldingTrainer.Domain
{
    /// Data model for a physical workpiece asset, including seams and QR registration info.
    [Serializable]
    public sealed class WorkpieceDefinition
    {
        public string WorkpieceId;
        public string DisplayName;
        /// Exact string payload the workpiece QR code encodes.
        public string QrPayload;
        /// Translation from QR marker origin to workpiece origin in marker-local space.
        public Vector3 QrToWorkpiecePositionMetres;
        /// Euler angles (degrees) from QR marker rotation to workpiece frame.
        public Vector3 QrToWorkpieceEulerDegrees;
        public SeamDefinition[] Seams = Array.Empty<SeamDefinition>();

        public StraightSeam BuildWorldSeam(int index, Matrix4x4 workpieceLocalToWorld)
        {
            SeamDefinition def = Seams[index];
            Vector3 start = workpieceLocalToWorld.MultiplyPoint3x4(def.LocalStartMetres);
            Vector3 end = workpieceLocalToWorld.MultiplyPoint3x4(def.LocalEndMetres);
            return new StraightSeam(start, end);
        }
    }

    [Serializable]
    public sealed class SeamDefinition
    {
        public string SeamId;
        public string DisplayName;
        /// Seam start position in workpiece-local space (metres).
        public Vector3 LocalStartMetres;
        /// Seam end position in workpiece-local space (metres).
        public Vector3 LocalEndMetres;
        public SeamTolerance Tolerance = new();
        /// Local axis on the controller that points along the tool tip direction.
        public Vector3 LocalToolForwardAxis = Vector3.forward;
        /// Rigid offset from controller tracking origin to physical welding tip (metres).
        public Vector3 ControllerToTipOffsetMetres = Vector3.zero;
        /// Position of the Hall sensor relative to the controller tracking origin.
        public Vector3 ControllerToHallOffsetMetres = Vector3.zero;
    }
}
