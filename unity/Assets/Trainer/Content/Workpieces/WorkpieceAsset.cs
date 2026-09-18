using UnityEngine;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Content
{
    /// Unity ScriptableObject wrapper for a WorkpieceDefinition.
    /// Create via Assets > Create > WeldingTrainer > Workpiece.
    [CreateAssetMenu(fileName = "Workpiece", menuName = "WeldingTrainer/Workpiece")]
    public sealed class WorkpieceAsset : ScriptableObject
    {
        [SerializeField] private WorkpieceDefinition _definition = new();

        public WorkpieceDefinition Definition => _definition;
    }
}
