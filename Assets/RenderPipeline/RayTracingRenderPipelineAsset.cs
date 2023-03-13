using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/RayTracingRenderPipelineAsset")]
public class RayTracingRenderPipelineAsset : RenderPipelineAsset
{
    [Header("RayTracing Assets")]
    public RayTracingShader rayTracingShader = null;

    [Header("Environment Settings")] 
    public Cubemap envTexture = null;

    [Header("Other settings")]
    [Tooltip("Whether to build the RTAS asynchronously on a Compute Queue or the regular Graphics Queue.")]
    public bool useAsyncRTASBuild = true;

    protected override System.Type renderPipelineType => typeof(RayTracingRenderPipelineInstance);

    protected override RenderPipeline CreatePipeline() => new RayTracingRenderPipelineInstance(this);
}