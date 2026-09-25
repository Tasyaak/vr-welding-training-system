using System;
using System.Collections.Generic;
using System.Linq;
using WeldingTrainer.Content.Spatial;

namespace WeldingTrainer.Platform.Meta.Tool.Tests
{
    public static class ToolTestCases
    {
        private static readonly Quat Identity = new Quat(0, 0, 0, 1);
        private static readonly Quat Z90 = new Quat(0, 0, Math.Sqrt(.5), Math.Sqrt(.5));
        private static void Require(bool value) { if (!value) throw new Exception("Assertion failed"); }
        private static void Near(Vec3 value, Vec3 expected) { if ((value - expected).Length > 1e-9) throw new Exception($"Expected {expected.x},{expected.y},{expected.z}; got {value.x},{value.y},{value.z}"); }
        private static ToolCalibration Calibration(Vec3 p, Quat q, Vec3 tip) => new ToolCalibration("glued-unit-1", "r1", "profile", "measured", true, ToolMath.Pose("Controller", "Tool", p, q), ToolMath.Pose("Tool", "Tip", tip, Identity));
        private static ToolHeadSnapshot Sample(MeasuredPose controller, ToolCalibration calibration, bool device = true, ToolInputBuffer input = null) => new ToolHeadSnapshot("session", 1, 10, 7, device, true, false, "profile", controller, MeasuredPose.Invalid("Head", PoseFailure.NotTracked), calibration, input ?? new ToolInputBuffer());
        private static MeasuredPose Controller(Vec3? p, Quat? q) => new MeasuredPose("Controller", p, q);
        public static IEnumerable<KeyValuePair<string, Action>> Cases()
        {
            yield return Case("known rigid chain", () => {
                var s = Sample(Controller(new Vec3(1,2,3), Z90), Calibration(new Vec3(.2,0,0), Identity, new Vec3(.1,0,0)));
                Near(s.Tool.Position.Value,new Vec3(1,2.2,3)); Near(s.Tip.Position.Value,new Vec3(1,2.3,3));
                Near(s.Tool.Pose.Value.TransformDirection(new Vec3(1,0,0)), new Vec3(0,1,0));
            });
            yield return Case("controller rotates tip offset", () => {
                var calibration=Calibration(new Vec3(.2,.1,.3),Identity,new Vec3(.1,.2,.3));
                var a=Sample(Controller(new Vec3(),Identity),calibration); var b=Sample(Controller(new Vec3(),Z90),calibration);
                Near(b.Tip.Position.Value,Z90.Rotate(a.Tip.Position.Value));
            });
            yield return Case("independent physical tip and full tool orientation", () => {
                var offset=ToolMath.Pose("Tool","Tip",new Vec3(.02,.03,-.04),Identity); var tip=new Vec3(-.08,.06,.12);
                foreach(var q in new[]{ Identity,Z90,new Quat(1,0,0,0) }) {
                    var transform=ToolCalibration.FromTipAndOrientation(tip,q,offset);
                    var s=Sample(Controller(new Vec3(),Identity),new ToolCalibration("unit","r","profile","evidence",true,transform,offset));
                    Near(s.Tip.Position.Value,tip); Near(s.Tool.Rotation.Value.Rotate(new Vec3(0,0,1)),q.Rotate(new Vec3(0,0,1)));
                }
            });
            yield return Case("identity chain", () => {
                var s=Sample(Controller(new Vec3(),Identity),Calibration(new Vec3(),Identity,new Vec3()));
                Near(s.Tip.Position.Value,new Vec3()); Near(s.Tip.Pose.Value.TransformDirection(new Vec3(1,2,3)),new Vec3(1,2,3));
            });
            yield return Case("missing position preserves only orientation", () => {
                var s=Sample(Controller(null,Z90),Calibration(new Vec3(.1,0,0),Identity,new Vec3(0,0,-.1)));
                Require(!s.Controller.PositionValid && s.Tool.RotationValid && !s.Tool.PositionValid && !s.Tip.PositionValid && !s.IsUsableAt(10,7));
            });
            yield return Case("missing rotation cannot place an offset", () => {
                var s=Sample(Controller(new Vec3(1,2,3),null),Calibration(new Vec3(.1,0,0),Identity,new Vec3(0,0,-.1)));
                Require(s.Controller.PositionValid && !s.Tool.PositionValid && !s.Tool.RotationValid && !s.Tip.Pose.HasValue);
            });
            yield return Case("disconnected cannot expose cached controller", () => {
                var s=Sample(Controller(new Vec3(1,2,3),Identity),Calibration(new Vec3(),Identity,new Vec3()),false);
                Require(!s.Controller.Pose.HasValue && !s.Tool.Pose.HasValue && !s.Tip.Pose.HasValue);
            });
            yield return Case("freshness and origin mismatch", () => {
                var s=Sample(Controller(new Vec3(),Identity),Calibration(new Vec3(),Identity,new Vec3()));
                Require(s.IsUsableAt(10,7)); Require(!s.IsUsableAt(10.2,7) && !s.IsUsableAt(10,8) && !s.IsUsableAt(9,7));
            });
            yield return Case("short analog trigger pulse retained once", () => {
                var b=new ToolInputBuffer(); b.SetContext(ToolInputContext.Training,1); b.Drain();
                b.ObserveTrigger(0,2); b.ObserveTrigger(.8,3); b.ObserveTrigger(.55,3.1); b.ObserveTrigger(.2,4);
                var events=b.Drain(); Require(events.Count==2 && events[0].Type==ToolCommandType.TriggerPressed && events[1].Type==ToolCommandType.TriggerReleased && events[0].Sequence<events[1].Sequence && b.Drain().Count==0);
            });
            yield return Case("short B pulse survives context invalidation", () => {
                var b=new ToolInputBuffer(); b.ObserveButtons(true,false,false,1); b.ObserveButtons(false,false,false,1.01); b.SetContext(ToolInputContext.Menu,2);
                var events=b.Drain(); Require(events.Count(x=>x.Type==ToolCommandType.EStopPressed)==1 && events[0].MonotonicSeconds==1);
            });
            yield return Case("tracking invalidation is not a physical trigger edge", () => {
                var b=new ToolInputBuffer(); b.ObserveTrigger(0,1); b.ObserveTrigger(.9,2); b.Drain(); b.Invalidate(3);
                Require(b.Drain().All(x=>x.Type==ToolCommandType.SystemInvalid));
                b.ObserveTrigger(.9,4); Require(b.Drain().Count==0 && !b.ProcessRequested);
                b.ObserveTrigger(0,5); Require(b.Drain().Single().Type==ToolCommandType.TriggerReleased);
            });
            yield return Case("B and trigger same capture always suppress request", () => {
                var b=new ToolInputBuffer(); b.SetContext(ToolInputContext.Training,1); b.Drain(); b.ObserveTrigger(0,2); b.ObserveButtons(true,false,false,3); b.ObserveTrigger(.9,3);
                var s=Sample(Controller(new Vec3(),Identity),Calibration(new Vec3(),Identity,new Vec3()),true,b); Require(!s.ProcessRequested && s.Commands.Any(x=>x.Type==ToolCommandType.EStopPressed));
            });
            yield return Case("held trigger across menu and resume needs release", () => {
                var b=new ToolInputBuffer(); b.SetContext(ToolInputContext.Training,1); b.ObserveTrigger(0,2); b.ObserveTrigger(1,3); Require(b.ProcessRequested);
                b.SetContext(ToolInputContext.Menu,4); b.ObserveTrigger(1,5); Require(!b.ProcessRequested); b.SetContext(ToolInputContext.Training,6); b.ObserveTrigger(1,7); Require(!b.ProcessRequested);
                b.ObserveTrigger(0,8); b.ObserveTrigger(1,9); Require(b.ProcessRequested); b.Invalidate(10); b.ObserveTrigger(1,11); Require(!b.ProcessRequested);
            });
            yield return Case("unqualified candidate never usable", () => {
                var c=Calibration(new Vec3(),Identity,new Vec3()); var u=new ToolCalibration("unit","draft",null,null,false,c.ControllerFromTool,c.ToolFromTip);
                var s=Sample(Controller(new Vec3(),Identity),u); Require(s.Tip.Pose.HasValue && !s.IsUsableAt(10,7));
            });
        }
        private static KeyValuePair<string,Action> Case(string name,Action action) => new KeyValuePair<string,Action>(name,action);
    }
}
