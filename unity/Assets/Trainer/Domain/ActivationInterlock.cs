using System;
using System.Collections.Generic;
using System.Linq;

namespace WeldingTrainer.Domain
{
    public sealed class FinitePatch
    {
        readonly Vec3[] boundary;
        public string Id { get; }
        public IReadOnlyList<Vec3> Boundary => Array.AsReadOnly(boundary);
        public Vec3 Normal { get; }
        public int ApproachSide { get; }

        public FinitePatch(string id, IEnumerable<Vec3> vertices, Vec3 normal, int approachSide)
        {
            Id = id;
            boundary = vertices?.ToArray() ?? Array.Empty<Vec3>();
            Normal = normal.Normalized;
            ApproachSide = approachSide;
            if (string.IsNullOrWhiteSpace(id) || boundary.Length < 3 || !normal.Finite || Normal.Length < .999 || (approachSide != 1 && approachSide != -1) || boundary.Any(v => !v.Finite))
                throw new ArgumentException("Invalid finite patch");
            // Supported authored geometry: strictly convex planar polygons in either winding.
            // Every other vertex must lie strictly on the same side of each edge.
            double winding = 0;
            for (int i = 0; i < boundary.Length; i++)
            {
                Vec3 edge = boundary[(i + 1) % boundary.Length] - boundary[i];
                if (!double.IsFinite(edge.Length) || edge.Length <= 1e-9)
                    throw new ArgumentException("Degenerate patch edge");
                double height = Vec3.Dot(boundary[i] - boundary[0], Normal);
                if (!double.IsFinite(height) || Math.Abs(height) > 1e-9)
                    throw new ArgumentException("Non-planar patch");
                for (int j = 0; j < boundary.Length; j++)
                {
                    if (j == i || j == (i + 1) % boundary.Length)
                        continue;
                    double side = Vec3.Dot(Vec3.Cross(edge / edge.Length, boundary[j] - boundary[i]), Normal);
                    if (!double.IsFinite(side) || Math.Abs(side) <= 1e-9)
                        throw new ArgumentException("Degenerate patch polygon");
                    if (winding == 0)
                        winding = Math.Sign(side);
                    if (Math.Sign(side) != winding)
                        throw new ArgumentException("Only convex simple polygons are supported");
                }
            }
        }
    }

    public enum ContactState
    {
        Invalid,
        Separated,
        Contact
    }

    public enum ContactReason
    {
        None,
        TrackingInvalid,
        OutsideBounds,
        WrongApproach,
        Ambiguous,
        InvalidNormal
    }

    public readonly struct ContactEvidence
    {
        public readonly ContactState State;
        public readonly ContactReason Reason;
        public readonly string SurfaceId;
        public readonly Vec3 ClosestPoint, Normal;
        public readonly double SignedStandoffMetres, EdgeDistanceMetres;
        public readonly bool InsideBounds, ApproachValid;
        public ContactEvidence(ContactState state, ContactReason reason, string id, Vec3 point, Vec3 normal, double standoff, double edge, bool inside, bool approach)
        {
            State = state;
            Reason = reason;
            SurfaceId = id;
            ClosestPoint = point;
            Normal = normal;
            SignedStandoffMetres = standoff;
            EdgeDistanceMetres = edge;
            InsideBounds = inside;
            ApproachValid = approach;
        }
    }

    public sealed class ContactPolicy
    {
        public double EnterMetres { get; }
        public double ExitMetres { get; }

        public ContactPolicy(double enter, double exit)
        {
            if (!double.IsFinite(enter) || !double.IsFinite(exit) || enter <= 0 || exit < enter)
                throw new ArgumentException("Contact exit must contain enter band");
            EnterMetres = enter;
            ExitMetres = exit;
        }
    }

    public sealed class FiniteContactEvaluator
    {
        FinitePatch retainedSurface;
        public ContactEvidence Evaluate(IEnumerable<FinitePatch> patches, Vec3 tip, bool trackingValid, ContactPolicy policy)
        {
            if (!trackingValid || !tip.Finite)
            {
                retainedSurface = null;
                return Invalid(ContactReason.TrackingInvalid);
            }

            if (patches == null || policy == null)
            {
                retainedSurface = null;
                return Invalid(ContactReason.InvalidNormal);
            }

            var hits = new List<ContactEvidence>();
            FinitePatch candidate = null;
            ContactEvidence nearest = default;
            double nearestDistance = double.PositiveInfinity;
            foreach (var patch in patches)
            {
                if (patch == null)
                {
                    retainedSurface = null;
                    return Invalid(ContactReason.InvalidNormal);
                }

                Vec3 n = patch.Normal;
                if (n.Length < .999)
                {
                    retainedSurface = null;
                    return Invalid(ContactReason.InvalidNormal);
                }

                double band = ReferenceEquals(patch, retainedSurface) ? policy.ExitMetres : policy.EnterMetres;
                Vec3 origin = patch.Boundary[0];
                double signed = Vec3.Dot(tip - origin, n);
                Vec3 projected = tip - n * signed;
                if (!double.IsFinite(signed) || !projected.Finite)
                {
                    retainedSurface = null;
                    return Invalid(ContactReason.InvalidNormal);
                }

                bool inside = Inside(projected, patch);
                bool approach = patch.ApproachSide > 0 ? signed >= -1e-9 : signed <= 1e-9;
                double edge = EdgeDistance(projected, patch);
                var evidence = new ContactEvidence(ContactState.Separated, !inside ? ContactReason.OutsideBounds : !approach ? ContactReason.WrongApproach : ContactReason.None, patch.Id, projected, n, signed, edge, inside, approach);
                if (inside && approach && Math.Abs(signed) <= band)
                {
                    hits.Add(new ContactEvidence(ContactState.Contact, ContactReason.None, patch.Id, projected, n, signed, edge, true, true));
                    candidate = patch;
                }

                if (Math.Abs(signed) < nearestDistance)
                {
                    nearestDistance = Math.Abs(signed);
                    nearest = evidence;
                }
            }

            if (hits.Count > 1)
            {
                retainedSurface = null;
                return Invalid(ContactReason.Ambiguous);
            }

            if (hits.Count == 1)
            {
                retainedSurface = candidate;
                return hits[0];
            }

            retainedSurface = null;
            return nearestDistance < double.PositiveInfinity ? nearest : Invalid(ContactReason.OutsideBounds);
        }

        public static bool TryRayImpact(FinitePatch patch, Vec3 origin, Vec3 direction, out Vec3 impact)
        {
            impact = default;
            if (patch == null || !origin.Finite || !direction.Finite || direction.Normalized.Length < .999)
                return false;
            double denominator = Vec3.Dot(direction, patch.Normal);
            if (!double.IsFinite(denominator) || Math.Abs(denominator) < 1e-10 || denominator * patch.ApproachSide >= 0)
                return false;
            double t = Vec3.Dot(patch.Boundary[0] - origin, patch.Normal) / denominator;
            if (!double.IsFinite(t) || t < 0)
                return false;
            impact = origin + direction * t;
            return impact.Finite && Inside(impact, patch);
        }

        static ContactEvidence Invalid(ContactReason r) => new(ContactState.Invalid, r, null, default, default, 0, 0, false, false);
        static bool Inside(Vec3 p, FinitePatch patch)
        {
            double sign = 0;
            for (int i = 0; i < patch.Boundary.Count; i++)
            {
                Vec3 a = patch.Boundary[i], b = patch.Boundary[(i + 1) % patch.Boundary.Count];
                double side = Vec3.Dot(Vec3.Cross((b - a).Normalized, p - a), patch.Normal);
                if (!double.IsFinite(side)) return false;
                if (Math.Abs(side) < 1e-9)
                    continue;
                if (sign == 0)
                    sign = Math.Sign(side);
                else if (Math.Sign(side) != sign)
                    return false;
            }

            return true;
        }

        static double EdgeDistance(Vec3 p, FinitePatch patch)
        {
            double best = double.PositiveInfinity;
            for (int i = 0; i < patch.Boundary.Count; i++)
            {
                Vec3 a = patch.Boundary[i], b = patch.Boundary[(i + 1) % patch.Boundary.Count], d = b - a;
                double t = Math.Max(0, Math.Min(1, Vec3.Dot(p - a, d) / d.LengthSquared));
                best = Math.Min(best, (p - (a + d * t)).Length);
            }

            return best;
        }
    }

    public enum RiskState
    {
        Unknown,
        Low,
        Warning,
        High
    }

    public readonly struct InterlockInputs
    {
        public readonly bool SessionEligible, RegistrationValid, AssemblyConfirmed, BindingMatches, HeadValid, ToolValid, SystemValid, RecorderAvailable, MenuOpen, TriggerPressed, ReleaseObserved, ExplicitlyArmed, FaultLatched, SessionSuspended;
        public readonly NozzleState Nozzle;
        public readonly ClampState Clamp;
        public readonly ProcessMode Mode;
        public readonly ContactEvidence Contact;
        public readonly RiskState Risk;
        public readonly BlockReason ProcessReasons;
        public InterlockInputs(bool session, bool registration, bool assembly, bool binding, bool head, bool tool, bool system, bool recorder, bool menu, bool trigger, bool released, bool armed, bool fault, NozzleState nozzle, ClampState clamp, ProcessMode mode, ContactEvidence contact, RiskState risk, BlockReason processReasons, bool suspended = false)
        {
            SessionSuspended = suspended;
            SessionEligible = session;
            RegistrationValid = registration;
            AssemblyConfirmed = assembly;
            BindingMatches = binding;
            HeadValid = head;
            ToolValid = tool;
            SystemValid = system;
            RecorderAvailable = recorder;
            MenuOpen = menu;
            TriggerPressed = trigger;
            ReleaseObserved = released;
            ExplicitlyArmed = armed;
            FaultLatched = fault;
            Nozzle = nozzle;
            Clamp = clamp;
            Mode = mode;
            Contact = contact;
            Risk = risk;
            ProcessReasons = processReasons;
        }
    }

    public readonly struct ActivationDecision
    {
        public readonly ActivationState State;
        public readonly BlockReason Reasons, PrimaryReason;
        public readonly bool Permission, Requested, Active;
        public ActivationDecision(ActivationState state, BlockReason reasons, bool requested)
        {
            State = state;
            Reasons = reasons;
            PrimaryReason = Primary(reasons);
            Permission = state == ActivationState.Armed || state == ActivationState.Active;
            Requested = requested;
            Active = state == ActivationState.Active;
        }

        public static BlockReason Primary(BlockReason reasons)
        {
            BlockReason[] order =
            {
                BlockReason.EmergencyStop,
                BlockReason.FaultLatched,
                BlockReason.ReflectionUnsafe,
                BlockReason.ReflectionUnknown,
                BlockReason.SystemInvalid,
                BlockReason.RegistrationInvalid,
                BlockReason.AssemblyUnconfirmed,
                BlockReason.BindingMismatch,
                BlockReason.HeadInvalid,
                BlockReason.ToolInvalid,
                BlockReason.MenuOpen,
                BlockReason.ContactInvalid,
                BlockReason.NozzleMismatch,
                BlockReason.ClampDisconnected,
                BlockReason.RecorderUnavailable,
                BlockReason.ProcessPrerequisiteMissing,
                BlockReason.TriggerReleaseRequired
            };
            foreach (var value in order)
                if ((reasons & value) != 0)
                    return value;
            ulong bits = (ulong)reasons;
            // Unlisted/future reasons: choose the lowest set bit deterministically.
            return (BlockReason)(bits & unchecked(~bits + 1));
        }
    }

    public static class ActivationReducer
    {
        public static ActivationDecision Evaluate(InterlockInputs i)
        {
            BlockReason r = BlockReason.None;
            if (!i.SessionEligible)
                r |= BlockReason.NoSession;
            if (!i.RegistrationValid)
                r |= BlockReason.RegistrationInvalid;
            if (!i.AssemblyConfirmed)
                r |= BlockReason.AssemblyUnconfirmed;
            if (!i.BindingMatches)
                r |= BlockReason.BindingMismatch;
            if (!i.HeadValid)
                r |= BlockReason.HeadInvalid;
            if (!i.ToolValid)
                r |= BlockReason.ToolInvalid;
            if (!i.SystemValid)
                r |= BlockReason.SystemInvalid;
            if (!i.RecorderAvailable)
                r |= BlockReason.RecorderUnavailable;
            if (i.MenuOpen)
                r |= BlockReason.MenuOpen;
            if (i.Nozzle == NozzleState.Unknown)
                r |= BlockReason.NozzleUnknown;
            else if (!Compatible(i.Mode, i.Nozzle))
                r |= BlockReason.NozzleMismatch;
            if (i.Clamp != ClampState.Connected)
                r |= BlockReason.ClampDisconnected;
            if (!ValidContact(i.Contact))
            {
                r |= BlockReason.ContactInvalid;
                if (i.Contact.Reason == ContactReason.OutsideBounds)
                    r |= BlockReason.OutsideFiniteSurface;
                if (i.Contact.Reason == ContactReason.WrongApproach)
                    r |= BlockReason.WrongApproach;
                if (i.Contact.Reason == ContactReason.Ambiguous)
                    r |= BlockReason.AmbiguousSurface;
            }

            if (i.Risk != RiskState.Low && i.Risk != RiskState.Warning && i.Risk != RiskState.High)
                r |= BlockReason.ReflectionUnknown;
            if (i.Risk == RiskState.High)
                r |= BlockReason.ReflectionUnsafe;
            if (i.FaultLatched)
                r |= BlockReason.FaultLatched;
            r |= i.ProcessReasons;
            if (!i.ReleaseObserved)
                r |= BlockReason.TriggerReleaseRequired;
            ActivationState state = (r & (BlockReason.FaultLatched | BlockReason.EmergencyStop)) != 0 ? ActivationState.ResetRequired : r != BlockReason.None || i.SessionSuspended ? ActivationState.Inhibited : !i.ExplicitlyArmed ? ActivationState.ReadyDisarmed : i.TriggerPressed ? ActivationState.Active : ActivationState.Armed;
            return new ActivationDecision(state, r, i.TriggerPressed);
        }

        public static bool Compatible(ProcessMode mode, NozzleState nozzle) => mode switch
        {
            ProcessMode.Fusion or ProcessMode.Wobble or ProcessMode.Pulsed => nozzle == NozzleState.Welding,
            ProcessMode.PreWeldCleaning or ProcessMode.PostWeldCleaning => nozzle == NozzleState.Cleaning,
            _ => false
        };
        static bool ValidContact(ContactEvidence c) => c.State == ContactState.Contact && c.Reason == ContactReason.None && !string.IsNullOrWhiteSpace(c.SurfaceId) && c.InsideBounds && c.ApproachValid && c.ClosestPoint.Finite && c.Normal.Finite && Math.Abs(c.Normal.Length - 1) < 1e-6 && double.IsFinite(c.SignedStandoffMetres) && double.IsFinite(c.EdgeDistanceMetres) && c.EdgeDistanceMetres >= 0;
    }
}
