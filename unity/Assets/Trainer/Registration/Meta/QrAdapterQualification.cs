using UnityEngine;

namespace WeldingTrainer.Registration.Meta
{
    // Evidence record, not an operator pose adjustment. No arbitrary offset or scale is accepted.
    [CreateAssetMenu(menuName = "Welding Trainer/QR adapter qualification")]
    public sealed class QrAdapterQualification : ScriptableObject
    {
        public int schemaVersion = 1;
        [Tooltip("Exact SystemInfo operatingSystem + deviceModel + plugin version tuple recorded by the preview.")]
        public string runtimeTuple;
        [Tooltip("Repository/device-test evidence verifying normalized Marker centre, -MRUK X / +MRUK Y printed orientation and +MRUK Z front normal.")]
        public string frameEvidence;
        [Tooltip("Evidence verifying which printed boundary PlaneRect reports on this SDK/OS tuple.")]
        public string dimensionEvidence;
        public string dimensionConvention = "Unqualified";
        public bool IsQualified(string tuple) => schemaVersion == 1 && !string.IsNullOrWhiteSpace(tuple) && tuple == runtimeTuple && !string.IsNullOrWhiteSpace(frameEvidence) && !string.IsNullOrWhiteSpace(dimensionEvidence) && (dimensionConvention == "IncludesQuietZone" || dimensionConvention == "ExcludesQuietZone");
    }
}
