using UnityEngine;

namespace WeldingTrainer.Content.Spatial.Editor
{
    // A RH engineering view of the unchanged CAD numeric basis. Unity's default
    // LookAt places -X on screen-right when viewing +Y with -Z north.
    // This belongs only to the CAD inspector, never to authored rigid poses.
    public static class CadPreviewView
    {
        public static void Configure(Camera camera, Vector3 eye, Vector3 target, Vector3 upHint)
        {
            var forward = (target - eye).normalized;
            var right = Vector3.Cross(forward, upHint).normalized;
            var up = Vector3.Cross(right, forward);
            var view = Matrix4x4.identity;
            view.SetRow(0, new Vector4(right.x, right.y, right.z, -Vector3.Dot(right, eye)));
            view.SetRow(1, new Vector4(up.x, up.y, up.z, -Vector3.Dot(up, eye)));
            view.SetRow(2, new Vector4(-forward.x, -forward.y, -forward.z, Vector3.Dot(forward, eye)));
            camera.transform.position = eye;
            camera.worldToCameraMatrix = view;
        }

        public static void Render(Camera camera)
        {
            // Changing the view parity also changes triangle rasterizer winding.
            bool previous = GL.invertCulling;
            try
            {
                GL.invertCulling = !previous;
                camera.Render();
            }
            finally
            {
                GL.invertCulling = previous;
            }
        }
    }
}
