using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[CanEditMultipleObjects]
[CustomEditor(typeof(Camera))]
[SupportedOnRenderPipeline(typeof(RayTracingRenderPipelineAsset))]
public class RayTracingCameraEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
    }
}
