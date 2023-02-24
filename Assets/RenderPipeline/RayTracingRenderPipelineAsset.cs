using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

[CreateAssetMenu(menuName = "Rendering/RayTracingRenderPipelineAsset")]
public class RayTracingRenderPipelineAsset : RenderPipelineAsset
{
    [Header("RayTracing Assets")]
    public RayTracingShader rayTracingShader = null;

    [Header("Environment Settings")] 
    public Cubemap envTexture = null;

    protected override RenderPipeline CreatePipeline() => new RayTracingRenderPipelineInstance(this);
}