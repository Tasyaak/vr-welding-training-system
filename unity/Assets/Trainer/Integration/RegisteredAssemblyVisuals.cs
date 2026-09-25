using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WeldingTrainer.Content.Spatial;
using WeldingTrainer.Content.Spatial.Unity;
using WeldingTrainer.Registration;
using WeldingTrainer.Registration.Meta;

namespace WeldingTrainer.Integration
{
    /// <summary>
    /// Presentation-only builder for the currently selected registered assembly.
    ///
    /// It creates Unity presentation copies of the imported fixture/workpiece meshes
    /// using UnityRegistrationMesh.Create(), then wires those roots into
    /// RegisteredAssemblyPoseDriver.
    ///
    /// This component never computes registration and must never be used as scoring truth.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(11000)]
    public sealed class RegisteredAssemblyVisuals : MonoBehaviour
    {
        [Header("Dependencies")]
        public QuestRegistrationBridge registration;
        public RegisteredAssemblyPoseDriver poseDriver;

        [Header("Presentation materials")]
        public Material fixtureMaterial;
        public Material workpieceMaterial;
        public bool showFixture = true;

        [Header("Diagnostics")]
        public bool logBuild = true;

        public Transform FixtureRoot => fixtureRoot;
        public Transform WorkpieceRoot => workpieceRoot;

        private Transform fixtureRoot;
        private Transform workpieceRoot;
        private string shownBindingId;

        private readonly List<Mesh> presentationMeshes = new List<Mesh>();

        private void LateUpdate()
        {
            if (!registration)
                return;

            RegistrationSnapshot snapshot = registration.Snapshot;
            if (snapshot == null || snapshot.Binding == null)
                return;

            if (string.Equals(
                    shownBindingId,
                    snapshot.Binding.Id,
                    StringComparison.Ordinal))
            {
                return;
            }

            Rebuild(snapshot.Binding);
        }

        private void Rebuild(AssemblyBindingSnapshot binding)
        {
            if (!registration.catalog)
                throw new InvalidOperationException(
                    "RegisteredAssemblyVisuals requires the bridge SpatialCatalogAsset.");

            if (!fixtureMaterial)
                throw new InvalidOperationException(
                    "RegisteredAssemblyVisuals requires Fixture Material.");

            if (!workpieceMaterial)
                throw new InvalidOperationException(
                    "RegisteredAssemblyVisuals requires Workpiece Material.");

            ClearPresentation();

            SpatialCatalogAsset catalog = registration.catalog;
            SpatialCatalogSnapshot frozen = catalog.Freeze();

            CatalogData definitions =
                new UnitySpatialJson().Read<CatalogData>(frozen.CatalogJson);

            var fixtureDefinition = definitions.fixtures.Single(
                item => item.id == binding.FixtureId);

            var workpieceDefinition = definitions.workpieces.Single(
                item => item.id == binding.PartId);

            Mesh[] sourceMeshes = catalog.CopyVisualMeshes();

            Mesh fixtureSource = sourceMeshes.Single(
                mesh => mesh.name == fixtureDefinition.sourceId);

            Mesh workpieceSource = sourceMeshes.Single(
                mesh => mesh.name == workpieceDefinition.sourceId);

            Mesh fixtureMesh = UnityRegistrationMesh.Create(fixtureSource);
            Mesh workpieceMesh = UnityRegistrationMesh.Create(workpieceSource);

            presentationMeshes.Add(fixtureMesh);
            presentationMeshes.Add(workpieceMesh);

            if (showFixture)
                fixtureRoot = CreateVisual(
                    "FixtureVisual",
                    fixtureMesh,
                    fixtureMaterial);

            workpieceRoot = CreateVisual(
                "WorkpieceVisual",
                workpieceMesh,
                workpieceMaterial);

            shownBindingId = binding.Id;

            if (poseDriver)
            {
                poseDriver.registration = registration;
                poseDriver.fixtureRoot = fixtureRoot;
                poseDriver.workpieceRoot = workpieceRoot;
            }

            if (logBuild)
            {
                Debug.Log(
                    $"ASSEMBLY VISUALS: binding={binding.Id} " +
                    $"fixture={binding.FixtureId}/{fixtureDefinition.sourceId} " +
                    $"workpiece={binding.PartId}/{workpieceDefinition.sourceId}",
                    this);
            }
        }

        private Transform CreateVisual(
            string objectName,
            Mesh mesh,
            Material material)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            return go.transform;
        }

        private void ClearPresentation()
        {
            if (fixtureRoot)
                Destroy(fixtureRoot.gameObject);

            if (workpieceRoot)
                Destroy(workpieceRoot.gameObject);

            fixtureRoot = null;
            workpieceRoot = null;

            foreach (Mesh mesh in presentationMeshes)
            {
                if (mesh)
                    Destroy(mesh);
            }

            presentationMeshes.Clear();
            shownBindingId = null;

            if (poseDriver)
            {
                poseDriver.fixtureRoot = null;
                poseDriver.workpieceRoot = null;
            }
        }

        private void OnDisable()
        {
            ClearPresentation();
        }
    }
}
