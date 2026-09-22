using System;
using System.Collections.Generic;
using UnityEngine;
using WeldingTrainer.Domain;

namespace WeldingTrainer.Presentation
{
    public sealed class ProgressiveBeadRenderer : MonoBehaviour
    {
        [SerializeField] Material acceptableMaterial;
        [SerializeField] Material poorMaterial;
        [SerializeField,Min(.0001f)] float widthMetres=.004f;
        [SerializeField,Min(1)] int maximumChunks=512;
        readonly List<LineRenderer> pool=new();DirectedSpline seam;long renderedRevision=-1;
        public void Configure(DirectedSpline workpieceLocalSeam){seam=workpieceLocalSeam??throw new ArgumentNullException(nameof(workpieceLocalSeam));renderedRevision=-1;}
        public void Apply(CoverageSnapshot snapshot)
        {if(seam==null)throw new InvalidOperationException("Configure a workpiece-local seam first");if(snapshot==null||snapshot.SeamId!=seam.Id)throw new ArgumentException("Coverage/seam mismatch");if(snapshot.Revision==renderedRevision)return;if(snapshot.Intervals.Count>maximumChunks)throw new InvalidOperationException("Bead chunk limit exceeded");Ensure(snapshot.Intervals.Count);for(int i=0;i<pool.Count;i++)pool[i].gameObject.SetActive(i<snapshot.Intervals.Count);for(int i=0;i<snapshot.Intervals.Count;i++){CoverageInterval interval=snapshot.Intervals[i];LineRenderer line=pool[i];line.sharedMaterial=interval.Quality==CoverageQuality.Acceptable?acceptableMaterial:poorMaterial;IReadOnlyList<Vec3> points=seam.Extract(interval.StartArcMetres,interval.EndArcMetres);line.positionCount=points.Count;for(int p=0;p<points.Count;p++)line.SetPosition(p,new Vector3((float)points[p].X,(float)points[p].Y,(float)points[p].Z));}renderedRevision=snapshot.Revision;}
        void Ensure(int count){while(pool.Count<count){var child=new GameObject("BeadChunk");child.transform.SetParent(transform,false);var line=child.AddComponent<LineRenderer>();line.useWorldSpace=false;line.widthMultiplier=widthMetres;line.numCapVertices=2;line.numCornerVertices=2;line.textureMode=LineTextureMode.Stretch;pool.Add(line);}}
    }
}
