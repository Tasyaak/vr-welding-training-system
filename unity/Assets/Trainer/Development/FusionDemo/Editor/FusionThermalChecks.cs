using System;
using UnityEditor;
using UnityEngine;

namespace WeldingTrainer.FusionDemo.Editor
{
    public static class FusionThermalChecks
    {
        [MenuItem("Tools/Welding Trainer/Fusion/Check thermal model")]
        public static void Run()
        {
            var low = NewModel();
            low.Step(0.08f, 1500, 0.5f, 0.5f, true);
            Require(!low.Formed[250] && low.PenetrationMm[250] == 0, "Formation requires exposure");
            var high = NewModel();
            high.Step(0.08f, 3000, 0.5f, 0.5f, true);
            Require(high.Formed[250] && high.PenetrationMm[250] > 0, "Higher power forms sooner");
            Require(!high.Formed[0] && !high.Formed[499], "Heating remains local");
            float warningTime = -1, burnTime = -1;
            for (int frame = 1; frame <= 200; frame++)
            {
                low.Step(0.01f, 1500, 0.5f, 0.5f, true);
                if (warningTime < 0 && low.IsWarning(250)) warningTime = frame * 0.01f;
                if (low.Burned[250]) { burnTime = frame * 0.01f; break; }
            }
            Require(warningTime > 0 && burnTime - warningTime > 0.2f, "Warning precedes burn-through");
            float depth = low.PenetrationMm[250];
            low.Step(10, 0, 0, 0, false);
            Require(low.HeatSeconds[250] < 0.02f && low.PenetrationMm[250] == depth,
                "Cooling does not erase measured depth");
            Require(low.Burned[250] && !low.IsWarning(250), "Hole survives cooling");
            low.Clear();
            Require(low.BurnedCount == 0 && !low.Burned[250] && low.DwellSeconds[250] == 0 && low.PenetrationMm[250] == 0,
                "Reset clears damage, depth and heat");
            low.Step(2, 0, 0.5f, 0.5f, true);
            Require(!low.Formed[250], "Zero power never heats");
            var thirty = Sweep(30, false);
            var ninety = Sweep(90, false);
            var reverse = Sweep(90, true);
            for (int i = 0; i < 500; i++)
            {
                Require(Math.Abs(thirty.PenetrationMm[i] - ninety.PenetrationMm[i]) < 0.015f,
                    "Frame-rate independent sweep depth at cell " + i);
                Require(Math.Abs(reverse.PenetrationMm[499-i] - ninety.PenetrationMm[i]) < 0.015f,
                    "Direction independent sweep at cell " + i);
            }
            var separated = NewModel();
            separated.Step(0.25f,1500,0.2f,0.2f,true);
            separated.Step(1,1500,0.2f,0.8f,false);
            separated.Step(0.25f,1500,0.8f,0.8f,true);
            Require(separated.Formed[100] && separated.Formed[400] && !separated.Formed[250],
                "Lift and reposition cannot heat the gap");
            Debug.Log("FUSION_THERMAL_CHECKS_PASSED: delay, power, locality, warning, damage, cooling, reset, 30/90 fps, reverse, gaps.");
        }

        private static SeamThermalModel NewModel() => new SeamThermalModel(500, 1);
        private static SeamThermalModel Sweep(int fps, bool reverse)
        {
            var model = NewModel();
            int frames = fps * 12;
            for (int i = 0; i < frames; i++)
            {
                float a = i / (float)frames, b = (i + 1) / (float)frames;
                model.Step(1f/fps,1500,reverse ? 1-a : a,reverse ? 1-b : b,true);
            }
            return model;
        }
        private static void Require(bool passed, string message)
        { if (!passed) throw new InvalidOperationException("Thermal test: " + message); }
    }
}
