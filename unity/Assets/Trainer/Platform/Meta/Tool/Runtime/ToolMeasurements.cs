using System;
using System.Collections.Generic;
using WeldingTrainer.Content.Spatial;

namespace WeldingTrainer.Platform.Meta.Tool
{
    public enum ToolInputContext { Training, Registration, Menu }
    public enum ToolCommandType { EStopPressed, TriggerPressed, TriggerReleased, Submit, MenuRequested, SystemInvalid }
    [Flags]
    public enum PoseFailure { None = 0, DeviceUnavailable = 1, NotTracked = 2, PositionMissing = 4, RotationMissing = 8, Lifecycle = 16, Stale = 32, CalibrationUnqualified = 64, ProfileMismatch = 128 }

    public static class ToolMath
    {
        public static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        public static RigidPose Pose(string to, string from, Vec3 p, Quat q) => new RigidPose(new PoseData { destination = to, source = from, position = p, rotation = q, scale = 1 });
        public static void Frame(RigidPose pose, string to, string from)
        {
            if (pose.Destination != to || pose.Source != from) throw new ArgumentException("Incorrect pose frames");
            pose.TransformPoint(new Vec3());
        }
    }

    public sealed class MeasuredPose
    {
        public string Frame { get; }
        public Vec3? Position { get; }
        public Quat? Rotation { get; }
        public bool PositionValid => Position.HasValue;
        public bool RotationValid => Rotation.HasValue;
        public RigidPose? Pose { get; }
        public PoseFailure Failure { get; }
        public MeasuredPose(string frame, Vec3? p, Quat? q, PoseFailure failure = PoseFailure.None)
        {
            if (string.IsNullOrWhiteSpace(frame)) throw new ArgumentException("Measurement frame required");
            Frame = frame;
            if ((failure & (PoseFailure.DeviceUnavailable | PoseFailure.NotTracked | PoseFailure.Lifecycle | PoseFailure.Stale)) != 0) { p = null; q = null; }
            if ((failure & PoseFailure.PositionMissing) != 0) p = null;
            if ((failure & PoseFailure.RotationMissing) != 0) q = null;
            if (p.HasValue) SpatialValidation.Vector(p.Value, "measurement");
            if (q.HasValue) ToolMath.Pose("World", frame, new Vec3(), q.Value);
            Position = p; Rotation = q; Failure = failure;
            if (p.HasValue && q.HasValue) Pose = ToolMath.Pose("World", frame, p.Value, q.Value);
        }
        public static MeasuredPose Invalid(string frame, PoseFailure why) => new MeasuredPose(frame, null, null, why);
    }

    // Immutable physical configuration. A profile is qualified only for the recorded glued assembly
    // and actual OpenXR interaction profile. Never substitute a presentation offset.
    public sealed class ToolCalibration
    {
        public string AssemblyId { get; }
        public string Revision { get; }
        public string InteractionProfile { get; }
        public string Evidence { get; }
        public bool Qualified { get; }
        public RigidPose ControllerFromTool { get; }
        public RigidPose ToolFromTip { get; }
        // Tool +Z points back toward the body; incident direction is -Z.
        public ToolCalibration(string assembly, string revision, string interactionProfile, string evidence, bool qualified, RigidPose controllerFromTool, RigidPose toolFromTip)
        {
            ToolMath.Frame(controllerFromTool, "Controller", "Tool");
            ToolMath.Frame(toolFromTip, "Tool", "Tip");
            if (string.IsNullOrWhiteSpace(assembly) || string.IsNullOrWhiteSpace(revision)) throw new ArgumentException("Physical assembly and revision required");
            if (qualified && (string.IsNullOrWhiteSpace(evidence) || string.IsNullOrWhiteSpace(interactionProfile))) throw new ArgumentException("Qualification evidence and interaction profile required");
            AssemblyId = assembly; Revision = revision; InteractionProfile = interactionProfile; Evidence = evidence; Qualified = qualified;
            ControllerFromTool = controllerFromTool; ToolFromTip = toolFromTip;
        }
        // Manual tip alignment and full orientation alignment are independent measurements.
        // Recover Tool origin from the chosen, explicit ToolFromTip geometry.
        public static RigidPose FromTipAndOrientation(Vec3 controllerTip, Quat controllerToolRotation, RigidPose toolFromTip)
        {
            ToolMath.Frame(toolFromTip, "Tool", "Tip");
            return ToolMath.Pose("Controller", "Tool", controllerTip - controllerToolRotation.Rotate(toolFromTip.Position), controllerToolRotation);
        }
    }

    public readonly struct ToolCommand
    {
        public readonly long Sequence, InputContextGeneration;
        public readonly double MonotonicSeconds;
        public readonly ToolCommandType Type;
        public ToolCommand(long seq, double time, long generation, ToolCommandType type)
        { Sequence = seq; MonotonicSeconds = time; InputContextGeneration = generation; Type = type; }
    }

    // Single consumer drains commands at Capture; rendering reads the resulting immutable snapshot.
    // Edges are captured at Input System callbacks, not at application tick frequency.
    public sealed class ToolInputBuffer
    {
        private readonly List<ToolCommand> pending = new List<ToolCommand>();
        private readonly double press, release;
        private long sequence;
        private bool trigger, b, a, menu, releaseRequired = true;
        public long Generation { get; private set; }
        public ToolInputContext Context { get; private set; } = ToolInputContext.Registration;
        public double? Analog { get; private set; }
        public bool TriggerHeld => trigger;
        public double PressThreshold => press;
        public double ReleaseThreshold => release;
        public bool ProcessRequested => Context == ToolInputContext.Training && trigger && !releaseRequired;
        public ToolInputBuffer(double press = .65, double release = .45)
        {
            if (!ToolMath.Finite(press) || !ToolMath.Finite(release) || release < 0 || release >= press || press > 1) throw new ArgumentException("Invalid hysteresis");
            this.press = press; this.release = release;
        }
        private void Add(ToolCommandType type, double now)
        {
            if (!ToolMath.Finite(now) || now < 0) throw new ArgumentException("Monotonic seconds required");
            pending.Add(new ToolCommand(++sequence, now, Generation, type));
        }
        public void SetContext(ToolInputContext value, double now)
        {
            if (Context == value) return;
            Context = value; Invalidate(now);
        }
        public void Invalidate(double now)
        {
            Generation++; releaseRequired = true; Analog = null;
            // Loss of evidence is not a physical trigger release. Preserve hysteresis state;
            // SystemInvalid and releaseRequired inhibit intent without inventing an edge.
            Add(ToolCommandType.SystemInvalid, now);
            // Preserve all queued safety and trigger history across lifecycle/context changes.
        }
        public void ObserveTrigger(double value, double now)
        {
            if (!ToolMath.Finite(value) || value < 0 || value > 1) { Invalidate(now); return; }
            Analog = value;
            if (value <= release) releaseRequired = false;
            bool next = trigger ? value > release : value >= press;
            if (next != trigger) { trigger = next; Add(next ? ToolCommandType.TriggerPressed : ToolCommandType.TriggerReleased, now); }
        }
        public void ObserveButtons(bool secondary, bool primary, bool stickClick, double now)
        {
            if (secondary && !b) { releaseRequired = true; Add(ToolCommandType.EStopPressed, now); }
            if (stickClick && !menu) { SetContext(ToolInputContext.Menu, now); releaseRequired = true; Add(ToolCommandType.MenuRequested, now); }
            if (primary && !a) Add(ToolCommandType.Submit, now);
            b = secondary; a = primary; menu = stickClick;
        }
        public IReadOnlyList<ToolCommand> Drain()
        { var result = Array.AsReadOnly(pending.ToArray()); pending.Clear(); return result; }
    }

    public sealed class ToolHeadSnapshot
    {
        public int SchemaVersion => 1;
        public string SessionId { get; }
        public long Sequence { get; }
        public double CapturedSeconds { get; }
        public double? SourceSeconds => null; // OpenXR source measurement time is not exposed here.
        public long OriginGeneration { get; }
        public long InputContextGeneration { get; }
        public bool DeviceValid { get; }
        public bool Focused { get; }
        public bool Paused { get; }
        public string InteractionProfile { get; }
        public MeasuredPose Controller { get; }
        public MeasuredPose Tool { get; }
        public MeasuredPose Tip { get; }
        public MeasuredPose Head { get; }
        public ToolCalibration Calibration { get; }
        public double? TriggerAnalog { get; }
        public bool TriggerHeld { get; }
        public double TriggerPressThreshold { get; }
        public double TriggerReleaseThreshold { get; }
        public bool ProcessRequested { get; }
        public ToolInputContext InputContext { get; }
        public IReadOnlyList<ToolCommand> Commands { get; }
        public bool IsUsableAt(double now, long origin, double maximumAge = .1) =>
            ToolMath.Finite(now) && ToolMath.Finite(maximumAge) && maximumAge > 0 && now >= CapturedSeconds && now - CapturedSeconds <= maximumAge && origin == OriginGeneration && DeviceValid && Focused && !Paused && Tool.Pose.HasValue && Tip.Pose.HasValue && Tool.Failure == PoseFailure.None && Tip.Failure == PoseFailure.None && Calibration != null && Calibration.Qualified && Calibration.InteractionProfile == InteractionProfile;

        public ToolHeadSnapshot(string session, long sequence, double now, long origin, bool device, bool focused, bool paused,
            string profile, MeasuredPose controller, MeasuredPose head, ToolCalibration calibration, ToolInputBuffer input)
        {
            if (!ToolMath.Finite(now) || now < 0) throw new ArgumentException("Invalid capture time");
            if (controller == null || controller.Frame != "Controller" || head == null || head.Frame != "Head") throw new ArgumentException("Expected named Controller and Head measurements");
            SessionId = session; Sequence = sequence; CapturedSeconds = now; OriginGeneration = origin;
            DeviceValid = device; Focused = focused; Paused = paused; InteractionProfile = profile;
            Controller = device && focused && !paused ? controller : MeasuredPose.Invalid("Controller", PoseFailure.DeviceUnavailable | PoseFailure.Lifecycle);
            Head = focused && !paused ? head : MeasuredPose.Invalid("Head", PoseFailure.Lifecycle);
            Calibration = calibration;
            var qualificationFailure = calibration == null || !calibration.Qualified ? PoseFailure.CalibrationUnqualified : calibration.InteractionProfile != profile ? PoseFailure.ProfileMismatch : PoseFailure.None;
            Tool = Compose(Controller, calibration?.ControllerFromTool, "Tool", qualificationFailure);
            Tip = Compose(Tool, calibration?.ToolFromTip, "Tip");
            TriggerAnalog = input.Analog; TriggerHeld = input.TriggerHeld; InputContext = input.Context;
            TriggerPressThreshold = input.PressThreshold; TriggerReleaseThreshold = input.ReleaseThreshold;
            InputContextGeneration = input.Generation; Commands = input.Drain();
            ProcessRequested = input.ProcessRequested && IsUsableAt(now, origin);
            foreach (var command in Commands)
                if (command.Type == ToolCommandType.EStopPressed || command.Type == ToolCommandType.SystemInvalid) ProcessRequested = false;
        }
        private static MeasuredPose Compose(MeasuredPose parent, RigidPose? offset, string frame, PoseFailure extraFailure = PoseFailure.None)
        {
            if (!offset.HasValue) return MeasuredPose.Invalid(frame, PoseFailure.CalibrationUnqualified);
            var o = offset.Value;
            ToolMath.Frame(o, parent.Frame, frame);
            Quat? r = parent.RotationValid ? parent.Rotation.Value * o.Rotation : (Quat?)null;
            Vec3? p = parent.Pose.HasValue ? parent.Pose.Value.TransformPoint(o.Position) : (Vec3?)null;
            return new MeasuredPose(frame, p, r, parent.Failure | extraFailure);
        }
    }
}
