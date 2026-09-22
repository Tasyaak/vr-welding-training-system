using System;
using System.Collections.Generic;
using System.Linq;

namespace WeldingTrainer.Domain
{
    public enum RegistrationValidityReason
    { None, Incomplete, UnstableCapture, DegenerateLayout, DimensionMismatch, ResidualTooLarge, SolverFailure }

    public sealed class CalibrationPointCapture
    {
        public string PointId { get; }
        public Vector3d RepresentativeWorld { get; }
        public int RawCount { get; }
        public int RetainedCount { get; }
        public double SpreadMetres { get; }
        public long OriginGeneration { get; }
        public CalibrationPointCapture(string id, Vector3d representative, int raw, int retained,
            double spread, long generation)
        { PointId=id; RepresentativeWorld=representative; RawCount=raw; RetainedCount=retained;
          SpreadMetres=spread; OriginGeneration=generation; }
    }

    public sealed class RegistrationQuality
    {
        public Vector3d[] ResidualVectors { get; }
        public double[] ResidualMetres { get; }
        public double RmsMetres { get; }
        public double MaximumMetres { get; }
        public double MaximumPairDistanceErrorMetres { get; }
        public double ConditioningRatio { get; }
        public RegistrationValidityReason Reason { get; }
        public bool Accepted => Reason == RegistrationValidityReason.None;
        public RegistrationQuality(Vector3d[] vectors, double[] residuals, double rms, double maximum,
            double pairError, double conditioning, RegistrationValidityReason reason)
        { ResidualVectors=vectors; ResidualMetres=residuals; RmsMetres=rms; MaximumMetres=maximum;
          MaximumPairDistanceErrorMetres=pairError; ConditioningRatio=conditioning; Reason=reason; }
    }

    public sealed class RegistrationCandidate
    {
        public const int SolverVersion = 1;
        public RigidPose WorldFromFixture { get; }
        public RigidPose WorldFromWorkpiece { get; }
        public RegistrationQuality Quality { get; }
        public IReadOnlyList<CalibrationPointCapture> Captures { get; }
        public RegistrationCandidate(RigidPose fixture, RigidPose workpiece, RegistrationQuality quality,
            CalibrationPointCapture[] captures)
        { WorldFromFixture=fixture; WorldFromWorkpiece=workpiece; Quality=quality;
          Captures=Array.AsReadOnly((CalibrationPointCapture[])captures.Clone()); }
    }

    public static class CalibrationCaptureMath
    {
        public static CalibrationPointCapture Build(string id, IReadOnlyList<Vector3d> samples,
            int minimumSamples, double maximumSpread, long generation)
        {
            if (samples == null || samples.Count < minimumSamples)
                throw new ArgumentException("Not enough fresh samples.");
            Vector3d provisional = CoordinateMedian(samples);
            double[] distances = samples.Select(x => Vector3d.Distance(x, provisional)).OrderBy(x => x).ToArray();
            double median = distances[distances.Length / 2];
            double cutoff = Math.Min(maximumSpread, Math.Max(0.00025, median * 2.5));
            Vector3d[] retained = samples.Where(x => Vector3d.Distance(x, provisional) <= cutoff).ToArray();
            if (retained.Length < minimumSamples) throw new ArgumentException("Too many unstable or outlier samples.");
            Vector3d representative = Mean(retained);
            double spread = retained.Max(x => Vector3d.Distance(x, representative));
            if (spread > maximumSpread) throw new ArgumentException("Capture spread exceeds policy.");
            return new CalibrationPointCapture(id, representative, samples.Count, retained.Length, spread, generation);
        }

        private static Vector3d Mean(IEnumerable<Vector3d> values)
        { Vector3d sum=default; int n=0; foreach(var value in values){ if(!value.IsFinite) throw new ArgumentException("Non-finite sample."); sum+=value;n++; } return sum/n; }
        private static Vector3d CoordinateMedian(IReadOnlyList<Vector3d> values)
        {
            if(values.Any(x=>!x.IsFinite))throw new ArgumentException("Non-finite sample.");
            double Median(IEnumerable<double> v){double[] a=v.OrderBy(x=>x).ToArray();int m=a.Length/2;return a.Length%2==0?(a[m-1]+a[m])*.5:a[m];}
            return new Vector3d(Median(values.Select(x=>x.X)),Median(values.Select(x=>x.Y)),Median(values.Select(x=>x.Z)));
        }
    }

    public static class FourPointRigidSolver
    {
        public const double NumericTolerance = 1e-9;
        public static RegistrationCandidate Solve(FixtureDefinition fixture,
            IReadOnlyList<CalibrationPointCapture> captures)
        {
            if (fixture == null || captures == null || captures.Count != 4)
                throw new ArgumentException("Exactly four labeled captures are required.");
            var ordered = fixture.ReferencePoints.Select(p => captures.SingleOrDefault(c => c.PointId == p.Id)
                ?? throw new ArgumentException($"Missing capture {p.Id}.")).ToArray();
            if (ordered.Select(x => x.OriginGeneration).Distinct().Count() != 1)
                return Reject(ordered, RegistrationValidityReason.Incomplete);
            Vector3d[] source=fixture.ReferencePoints.Select(x=>x.FixturePositionMetres).ToArray();
            Vector3d[] target=ordered.Select(x=>x.RepresentativeWorld).ToArray();
            double conditioning=Math.Min(Conditioning(source), Conditioning(target));
            if (conditioning < 0.05) return Reject(ordered, RegistrationValidityReason.DegenerateLayout, conditioning);
            double pairError=MaximumPairError(source,target);
            if (pairError > fixture.Calibration.MaxResidualMetres * 2)
                return Reject(ordered, RegistrationValidityReason.DimensionMismatch, conditioning, pairError);
            RigidPose transform;
            try { transform=Fit(source,target); } catch { return Reject(ordered, RegistrationValidityReason.SolverFailure, conditioning, pairError); }
            var vectors=new Vector3d[4]; var residuals=new double[4]; double sum=0,max=0;
            for(int i=0;i<4;i++){ vectors[i]=target[i]-(RigidPoseMath.Rotate(transform.Rotation,source[i])+transform.PositionMetres);
                residuals[i]=vectors[i].Length; sum+=residuals[i]*residuals[i]; max=Math.Max(max,residuals[i]); }
            double rms=Math.Sqrt(sum/4);
            var reason=max>fixture.Calibration.MaxResidualMetres ? RegistrationValidityReason.ResidualTooLarge : RegistrationValidityReason.None;
            var quality=new RegistrationQuality(vectors,residuals,rms,max,pairError,conditioning,reason);
            return new RegistrationCandidate(transform,RigidPoseMath.Compose(transform,fixture.WorkpieceFromFixture),quality,ordered);
        }

        public static RigidPose Fit(IReadOnlyList<Vector3d> source, IReadOnlyList<Vector3d> target)
        {
            if(source.Count!=target.Count || source.Count<3) throw new ArgumentException("Correspondence count mismatch.");
            Vector3d a=Mean(source), b=Mean(target); double[,] s=new double[3,3];
            for(int i=0;i<source.Count;i++){Vector3d x=source[i]-a,y=target[i]-b;
                s[0,0]+=x.X*y.X;s[0,1]+=x.X*y.Y;s[0,2]+=x.X*y.Z;
                s[1,0]+=x.Y*y.X;s[1,1]+=x.Y*y.Y;s[1,2]+=x.Y*y.Z;
                s[2,0]+=x.Z*y.X;s[2,1]+=x.Z*y.Y;s[2,2]+=x.Z*y.Z;}
            double tr=s[0,0]+s[1,1]+s[2,2];
            double[,] n={{tr,s[1,2]-s[2,1],s[2,0]-s[0,2],s[0,1]-s[1,0]},
                {s[1,2]-s[2,1],s[0,0]-s[1,1]-s[2,2],s[0,1]+s[1,0],s[2,0]+s[0,2]},
                {s[2,0]-s[0,2],s[0,1]+s[1,0],-s[0,0]+s[1,1]-s[2,2],s[1,2]+s[2,1]},
                {s[0,1]-s[1,0],s[2,0]+s[0,2],s[1,2]+s[2,1],-s[0,0]-s[1,1]+s[2,2]}};
            double shift=0;for(int r=0;r<4;r++){double row=0;for(int c=0;c<4;c++)row+=Math.Abs(n[r,c]);shift=Math.Max(shift,row);}
            double[] q={1,0,0,0};
            for(int k=0;k<80;k++){double[] z=new double[4];for(int r=0;r<4;r++){for(int c=0;c<4;c++)z[r]+=n[r,c]*q[c];z[r]+=shift*q[r];}
                double length=Math.Sqrt(z.Sum(v=>v*v));if(length<NumericTolerance)throw new InvalidOperationException("Degenerate fit.");for(int j=0;j<4;j++)q[j]=z[j]/length;}
            var rotation=new Quaterniond(q[1],q[2],q[3],q[0]);
            return new RigidPose(b-RigidPoseMath.Rotate(rotation,a),rotation);
        }

        private static RegistrationCandidate Reject(CalibrationPointCapture[] c, RegistrationValidityReason reason,
            double conditioning=0,double pair=0) => new(default,default,new RegistrationQuality(
                new Vector3d[4],new double[4],double.PositiveInfinity,double.PositiveInfinity,pair,conditioning,reason),c);
        private static Vector3d Mean(IReadOnlyList<Vector3d> p){Vector3d s=default;for(int i=0;i<p.Count;i++)s+=p[i];return s/p.Count;}
        private static double MaximumPairError(Vector3d[] a,Vector3d[] b){double max=0;for(int i=0;i<4;i++)for(int j=i+1;j<4;j++)max=Math.Max(max,Math.Abs(Vector3d.Distance(a[i],a[j])-Vector3d.Distance(b[i],b[j])));return max;}
        private static double Conditioning(Vector3d[] p)
        {
            Vector3d mean=Mean(p); double[,] c=new double[3,3]; foreach(var value in p){Vector3d v=value-mean;
                double[] x={v.X,v.Y,v.Z};for(int i=0;i<3;i++)for(int j=0;j<3;j++)c[i,j]+=x[i]*x[j];}
            double[] eigen=JacobiEigenvalues(c).OrderByDescending(x=>x).ToArray();
            return eigen[0] <= NumericTolerance ? 0 : Math.Sqrt(Math.Max(0,eigen[1])/eigen[0]);
        }
        private static double[] JacobiEigenvalues(double[,] a)
        {
            a=(double[,])a.Clone();for(int iteration=0;iteration<30;iteration++){int p=0,q=1;
                if(Math.Abs(a[0,2])>Math.Abs(a[p,q])){p=0;q=2;}if(Math.Abs(a[1,2])>Math.Abs(a[p,q])){p=1;q=2;}
                if(Math.Abs(a[p,q])<1e-15)break;double phi=.5*Math.Atan2(2*a[p,q],a[q,q]-a[p,p]),cs=Math.Cos(phi),sn=Math.Sin(phi);
                for(int k=0;k<3;k++){double apk=a[p,k],aqk=a[q,k];a[p,k]=cs*apk-sn*aqk;a[q,k]=sn*apk+cs*aqk;}
                for(int k=0;k<3;k++){double akp=a[k,p],akq=a[k,q];a[k,p]=cs*akp-sn*akq;a[k,q]=sn*akp+cs*akq;}}
            return new[]{a[0,0],a[1,1],a[2,2]};
        }
    }
}
