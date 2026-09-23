using System;
using UnityEngine;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Presentation
{
    [RequireComponent(typeof(LineRenderer))]
    public sealed class WobbleEnvelopeRenderer : MonoBehaviour
    {
        [SerializeField] Material envelopeMaterial;LineRenderer line;
        public void Configure(DirectedSpline seam,WobbleProfile profile)
        {if(seam==null||profile==null||seam.Id!=profile.SeamId)throw new ArgumentException("Wobble seam/profile mismatch");line=GetComponent<LineRenderer>();line.useWorldSpace=false;line.sharedMaterial=envelopeMaterial;line.widthMultiplier=(float)(2*profile.HalfAmplitudeMetres+profile.SpotWidthMetres);line.positionCount=seam.Points.Count;for(int i=0;i<seam.Points.Count;i++){Vec3 p=seam.Points[i];line.SetPosition(i,new Vector3((float)p.X,(float)p.Y,(float)p.Z));}}
    }
}
