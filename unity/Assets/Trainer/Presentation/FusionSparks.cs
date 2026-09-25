using UnityEngine;

namespace WeldingTrainer.Presentation
{
    public sealed class FusionSparks : MonoBehaviour
    {
        public Material sparkMaterial;
        [Range(20, 500)]
        public float sparksPerSecond = 140;
        public bool contactLight = true;
        private ParticleSystem streaks;
        private ParticleSystem embers;
        private Light glow;
        private bool emitting;
        private void Awake()
        {
            streaks = CreateParticles("Hot streaks", true);
            embers = CreateParticles("Cooling embers", false);
            glow = new GameObject("Pool light").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.type = LightType.Point;
            glow.color = new Color(1, 0.24f, 0.045f);
            glow.range = 0.18f;
            glow.shadows = LightShadows.None;
            glow.enabled = false;
        }

        private ParticleSystem CreateParticles(string label, bool stretched)
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            var ps = child.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = stretched ? 300 : 100;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, stretched ? 0.45f : 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(stretched ? 0.6f : 0.2f, stretched ? 2.4f : 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.0015f, stretched ? 0.0035f : 0.005f);
            main.gravityModifier = stretched ? 0.7f : 0.9f;
            main.startColor = Color.white;
            var emission = ps.emission;
            emission.rateOverTime = stretched ? sparksPerSecond : sparksPerSecond * 0.2f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = stretched ? 42 : 65;
            shape.radius = 0.002f;
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(new Color(1, 0.92f, 0.65f), 0), new GradientColorKey(new Color(1, 0.38f, 0.04f), 0.25f), new GradientColorKey(new Color(0.55f, 0.035f, 0.004f), 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.9f, 0.45f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0.15f));
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = sparkMaterial;
            renderer.renderMode = stretched ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            renderer.lengthScale = 2;
            renderer.velocityScale = 0.045f;
            renderer.cameraVelocityScale = 0;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        public void SetWelding(bool active, Vector3 worldPoint, Vector3 worldNormal)
        {
            transform.SetPositionAndRotation(worldPoint, Quaternion.LookRotation(worldNormal));
            if (active != emitting)
            {
                if (active)
                {
                    streaks.Play();
                    embers.Play();
                }
                else
                {
                    streaks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    embers.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }

                emitting = active;
            }

            glow.enabled = active && contactLight;
            glow.intensity = 0.7f + 0.2f * Mathf.Sin(Time.time * 83);
        }

        public void Clear()
        {
            emitting = false;
            if (streaks != null)
                streaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (embers != null)
                embers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (glow != null)
                glow.enabled = false;
        }

        private void OnDisable() => Clear();
    }
}
