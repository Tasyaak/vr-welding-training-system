using System;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.Android;
using WeldingTrainer.Content.Spatial;

namespace WeldingTrainer.Registration.Meta
{
    public sealed class UnityMonotonicClock : IMonotonicClock
    {
        public double Now => Time.realtimeSinceStartupAsDouble;
    }

    public static class UnityRegistrationPose
    {
        // Explicit handedness boundary: S_z = diag(1,1,-1), not a rigid reflection quaternion.
        public static Vec3 ToDomain(Vector3 v) => new Vec3(v.x, v.y, -v.z);
        public static Vector3 ToUnity(Vec3 v) => new Vector3((float)v.x, (float)v.y, (float)-v.z);
        public static Quat ToDomain(Quaternion q)
        {
            double x = q.x, y = q.y, z = q.z, w = q.w;
            double lengthSquared = x * x + y * y + z * z + w * w;
            // Only remove float SDK roundoff before entering the stricter double-precision content contract.
            if (!RegistrationMath.Finite(lengthSquared) || Math.Abs(lengthSquared - 1) > 1e-5)
                throw new ArgumentException("SDK quaternion must be finite and unit length (float roundoff only)");
            double length = Math.Sqrt(lengthSquared);
            return new Quat(-x / length, -y / length, z / length, w / length);
        }

        public static Quaternion ToUnity(Quat q) => new Quaternion((float)-q.x, (float)-q.y, (float)q.z, (float)q.w);
        public static RigidPose Read(Transform transform, string source)
        {
            var scale = transform.lossyScale;
            if (!RegistrationMath.Finite(scale.x) || !RegistrationMath.Finite(scale.y) || !RegistrationMath.Finite(scale.z) || (scale - Vector3.one).sqrMagnitude > 1e-10f)
                throw new ArgumentException("Spatial transform requires unit scale");
            var p = transform.position;
            var r = transform.rotation;
            return RegistrationMath.Pose("World", source, ToDomain(p), ToDomain(r));
        }

        public static void Write(Transform transform, RigidPose pose)
        {
            transform.SetPositionAndRotation(ToUnity(pose.Position), ToUnity(pose.Rotation));
            transform.localScale = Vector3.one;
        }
    }

    public sealed class MetaQrTracker : IQrTracker
    {
        private readonly MRUK mruk;
        private readonly OVRCameraRig rig;
        private readonly bool qualified;
        private readonly string dimensionConvention, conventionEvidence;
        private readonly IMonotonicClock clock;
        private readonly List<MRUKTrackable> trackables = new List<MRUKTrackable>();
        private readonly MrukQrTrackableCache cache;
        private readonly Action<string> diagnostic;
        private readonly HashSet<int> present = new HashSet<int>();
        private bool requested, disposed, permissionPending, denied;
        private long sequence, origin;
        private Vector3 trackingPosition;
        private Quaternion trackingRotation;
        private OVRDisplay display;
        private readonly bool manifestValid;
        public string RuntimeTuple { get; }
        public TrackerFrame LastFrame { get; private set; }

        public MetaQrTracker(MRUK mruk, OVRCameraRig rig, QrAdapterQualification qualification, IMonotonicClock clock, Action<string> diagnostic = null)
        {
            this.mruk = mruk ? mruk : throw new ArgumentNullException(nameof(mruk));
            this.rig = rig ? rig : throw new ArgumentNullException(nameof(rig));
            this.clock = clock;
            this.diagnostic = diagnostic;
            cache = new MrukQrTrackableCache(Trace);
            mruk.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
            mruk.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);
            RuntimeTuple = SystemInfo.operatingSystem + " | " + SystemInfo.deviceModel + " | OVRPlugin " + OVRPlugin.version + " | Core/MRUK 205.0.0 | OpenXR 1.16.1 | RH-Sz/MRUK-Sx v1";
            qualified = qualification && qualification.IsQualified(RuntimeTuple);
            dimensionConvention = qualified ? qualification.dimensionConvention : "Unqualified";
            conventionEvidence = qualified ? qualification.frameEvidence + "; " + qualification.dimensionEvidence : null;
            manifestValid = HasManifestPermissions();
            trackingPosition = rig.trackingSpace.position;
            trackingRotation = rig.trackingSpace.rotation;
            OVRManager.TrackingLost += Discontinuity;
            OVRManager.TrackingOriginChangePending += OriginChanging;
            AttachDisplay();
        }

        private void AttachDisplay()
        {
            if (display == null && OVRManager.display != null)
            {
                display = OVRManager.display;
                display.RecenteredPose += Discontinuity;
            }
        }

        private void OriginChanging(OVRManager.TrackingOrigin ignored, OVRPose? previous) => Discontinuity();
        private void Discontinuity()
        {
            origin++;
            SeedExisting();
            Trace("origin-reset");
        }

        public void RequestScanning()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(MetaQrTracker));
            requested = true;
            Trace("scan-start");
            denied = false;
            SeedExisting();
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission) && !permissionPending)
            {
                permissionPending=true;
                var callbacks=new PermissionCallbacks();
                callbacks.PermissionGranted+=_=> { if(!disposed) { permissionPending=false; denied=false; } };
                callbacks.PermissionDenied+=_=> { if(!disposed) { permissionPending=false; denied=true; } };
                Permission.RequestUserPermission(OVRPermissionsRequester.ScenePermission,callbacks);
            }
#endif
            ApplyRequest();
        }

        private void ApplyRequest()
        {
            if (!mruk)
                return;
            var config = mruk.SceneSettings.TrackerConfiguration;
            config.QRCodeTrackingEnabled = requested;
            mruk.SceneSettings.TrackerConfiguration = config;
        }

        public void StopScanning()
        {
            requested = false;
            Trace("scan-stop");
            ApplyRequest();
            cache.Clear();
            LastFrame = null;
        }

        public TrackerFrame Read()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(MetaQrTracker));
            AttachDisplay();
            ApplyRequest();
            // Any origin movement is untrusted here. #58 may later supply a proven coherent rebase.
            if (rig.trackingSpace.position != trackingPosition || rig.trackingSpace.rotation != trackingRotation)
            {
                trackingPosition = rig.trackingSpace.position;
                trackingRotation = rig.trackingSpace.rotation;
                Discontinuity();
            }

            bool permission = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            permission=Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission);
#endif
            bool tracking = OVRPlugin.initialized && OVRManager.isHmdPresent && OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.Head) && OVRPlugin.GetNodeOrientationTracked(OVRPlugin.Node.Head);
            var status = new PlatformStatus(mruk.QRCodeTrackingSupported, permission ? PermissionState.Granted : denied ? PermissionState.Denied : permissionPending ? PermissionState.Pending : PermissionState.Unknown, manifestValid && mruk.isActiveAndEnabled && !mruk.EnableWorldLock && rig.trackingSpace.lossyScale == Vector3.one, mruk.SceneSettings.TrackerConfiguration.QRCodeTrackingEnabled, mruk.TrackerConfiguration.QRCodeTrackingEnabled, qualified, tracking, origin, clock.Now, RuntimeTuple, conventionEvidence);
            var observations = new List<QrObservation>();
            present.Clear();
            mruk.GetTrackables(trackables);
            if (requested)
                foreach (var t in trackables)
                {
                    if (!t || t.TrackableType != OVRAnchor.TrackableType.QRCode)
                        continue;
                    int id = t.GetInstanceID();
                    present.Add(id); // Includes temporarily untracked objects, as MRUK does.
                    var observation = cache.Poll(id, t.IsTracked, t.PlaneBoundary2D, identity => Capture(t, identity));
                    if (observation != null)
                        observations.Add(observation);
                }

            cache.Reconcile(present);
            LastFrame = new TrackerFrame(status, observations, cache.Snapshot());
            return LastFrame;
        }

        private void SeedExisting()
        {
            cache.Clear();
            mruk.GetTrackables(trackables);
            foreach (var existing in trackables)
                if (existing && existing.TrackableType == OVRAnchor.TrackableType.QRCode)
                    cache.Add(existing.GetInstanceID(), existing.PlaneBoundary2D, false);
        }

        private void OnTrackableAdded(MRUKTrackable trackable)
        {
            if (!trackable || trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
                return;
            Trace($"event=TrackableAdded instance={trackable.GetInstanceID()}");
            if (requested && !disposed)
                cache.Add(trackable.GetInstanceID(), trackable.PlaneBoundary2D, true);
        }

        private void OnTrackableRemoved(MRUKTrackable trackable)
        {
            if (!trackable || trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
                return;
            Trace($"event=TrackableRemoved instance={trackable.GetInstanceID()}");
            cache.Remove(trackable.GetInstanceID());
        }

        private void Trace(string message)
        {
            try
            {
                diagnostic?.Invoke($"QR t={clock.Now:F6} frame={Time.frameCount} origin={origin} requested={requested} {message}");
            }
            catch
            { /* Logging is diagnostic only. */
            }
        }

        public static bool IsFinitePlane(Rect rect) => RegistrationMath.Finite(rect.center.x) && RegistrationMath.Finite(rect.center.y) && RegistrationMath.Finite(rect.width) && RegistrationMath.Finite(rect.height) && rect.width > 0 && rect.height > 0;
        private QrObservation Capture(MRUKTrackable trackable, string identity)
        {
            RigidPose? pose = null;
            double width = 0, height = 0;
            if (trackable.PlaneRect.HasValue)
            {
                var rect = trackable.PlaneRect.Value;
                width = rect.width;
                height = rect.height;
                if (IsFinitePlane(rect) && trackable.transform.parent == null)
                    try
                    {
                        pose = RegistrationMath.CenterMrukPlane(UnityRegistrationPose.Read(trackable.transform, "MrukPlane"), rect.center.x, rect.center.y);
                    }
                    catch (ArgumentException)
                    {
                    }
                    catch (SpatialContentException)
                    {
                    }
            }

            // Even an invalid SDK update replaces old evidence; never hide malformed bounds behind a good pose.
            return new QrObservation(TransportPayload(trackable.MarkerPayloadBytes, trackable.MarkerPayloadString != null), identity, ++sequence, origin, clock.Now, pose, width, height, dimensionConvention, UnityRegistrationPose.ToDomain(rig.centerEyeAnchor.position));
        }

        // MRUK's StringQRCode transport includes one trailing NUL. Binary payloads are unmodified.
        public static byte[] TransportPayload(byte[] raw, bool stringPayload)
        {
            if (raw == null || raw.Length > QrPayload.MaximumBytes + 1)
                return null;
            int count = raw.Length;
            if (stringPayload && count > 0 && raw[count - 1] == 0)
                count--;
            if (count > QrPayload.MaximumBytes)
                return null;
            var bytes = new byte[count];
            Array.Copy(raw, bytes, count);
            return bytes;
        }

        private static bool HasManifestPermissions()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using(var unity=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using(var activity=unity.GetStatic<AndroidJavaObject>("currentActivity"))
                using(var manager=activity.Call<AndroidJavaObject>("getPackageManager"))
                using(var info=manager.Call<AndroidJavaObject>("getPackageInfo",activity.Call<string>("getPackageName"),4096))
                {
                    var permissions=info.Get<string[]>("requestedPermissions");
                    return Array.IndexOf(permissions,"com.oculus.permission.USE_SCENE")>=0 && Array.IndexOf(permissions,"com.oculus.permission.USE_ANCHOR_API")>=0;
                }
            }
            catch(Exception) { return false; }
#else
            return false;
#endif
        }

        public void Dispose()
        {
            if (disposed)
                return;
            StopScanning();
            disposed = true;
            if (mruk)
            {
                mruk.SceneSettings.TrackableAdded.RemoveListener(OnTrackableAdded);
                mruk.SceneSettings.TrackableRemoved.RemoveListener(OnTrackableRemoved);
            }

            OVRManager.TrackingLost -= Discontinuity;
            OVRManager.TrackingOriginChangePending -= OriginChanging;
            if (display != null)
                display.RecenteredPose -= Discontinuity;
        }
    }
}
