using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

using UnityEngine.Experimental.Rendering.RenderGraphModule;

public class RayTracingRenderPipelineInstance : RenderPipeline
{   
    private RayTracingRenderPipelineAsset renderPipelineAsset;

    private RayTracingAccelerationStructure rtas = null;

    private RenderGraph renderGraph = null;

    private RTHandleSystem rtHandleSystem = null;

    public RayTracingRenderPipelineInstance(RayTracingRenderPipelineAsset asset)
    {
        renderPipelineAsset = asset;

        RayTracingAccelerationStructure.Settings settings = new RayTracingAccelerationStructure.Settings()
        {
            rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything,
            managementMode = RayTracingAccelerationStructure.ManagementMode.Manual,
            layerMask = 255
        };
      
        rtas = new RayTracingAccelerationStructure(settings);

        renderGraph = new RenderGraph("THE Render Graph");

        rtHandleSystem = new RTHandleSystem();
    }

    protected override void Dispose(bool disposing)
    {
        if (rtas != null)
        {
            rtas.Release();
            rtas = null;
        }

        renderGraph.Cleanup();
        renderGraph = null;

        rtHandleSystem.Dispose();
    }
    class RTASBuildPassData
    {
        public RayTracingAccelerationStructureHandle rtasHandleOutput;
    }

    class RayTracingRenderPassData
    {
        public TextureHandle outputTexture;

        public RayTracingAccelerationStructureHandle rtasHandleInput;
    };

    protected override void Render (ScriptableRenderContext context, Camera[] cameras)
    {
        bool error = false;

        error = error || !renderPipelineAsset.rayTracingShader;
        error = error || !SystemInfo.supportsRayTracing;
        error = error || rtas == null;

        if (error)
        {
            CommandBuffer commandBuffer = new CommandBuffer();

            if (!SystemInfo.supportsRayTracing)
                Debug.Log("The RayTracing API is not supported by this GPU or by the current graphics API.");

            if (!renderPipelineAsset.rayTracingShader)
                Debug.LogError("No RayTracing shader! Set the raytrace shader in Main Camera.");

            if (rtas == null)
                Debug.LogError("The RayTracingAccelerationStructure object is not valid.");

            commandBuffer.ClearRenderTarget(true, true, Color.magenta);
            context.ExecuteCommandBuffer(commandBuffer);
            context.Submit();
            commandBuffer.Release();
            return;
        }

        {
            RayTracingInstanceCullingConfig cullingConfig = new RayTracingInstanceCullingConfig();

            cullingConfig.flags = RayTracingInstanceCullingFlags.None;

            // Disable anyhit shaders for opaque geometries for best ray tracing performance.
            cullingConfig.subMeshFlagsConfig.opaqueMaterials = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;

            // Disable transparent geometries.
            cullingConfig.subMeshFlagsConfig.transparentMaterials = RayTracingSubMeshFlags.Disabled;

            // Enable anyhit shaders for alpha-tested / cutout geometries.
            cullingConfig.subMeshFlagsConfig.alphaTestedMaterials = RayTracingSubMeshFlags.Enabled;        

            List<RayTracingInstanceCullingTest> instanceTests = new List<RayTracingInstanceCullingTest>();

            RayTracingInstanceCullingTest instanceTest = new RayTracingInstanceCullingTest();
            instanceTest.allowTransparentMaterials = false;
            instanceTest.allowOpaqueMaterials = true;
            instanceTest.allowAlphaTestedMaterials = true;
            instanceTest.layerMask = -1;
            instanceTest.shadowCastingModeMask = (1 << (int)ShadowCastingMode.Off) | (1 << (int)ShadowCastingMode.On) | (1 << (int)ShadowCastingMode.TwoSided);
            instanceTest.instanceMask = 1 << 0;

            instanceTests.Add(instanceTest);

            cullingConfig.instanceTests = instanceTests.ToArray();

            rtas.ClearInstances();
            rtas.CullInstances(ref cullingConfig);
        }
        
        foreach (Camera camera in cameras)
        {
            var additionalData = camera.GetComponent<AdditionalCameraData>();
            if (additionalData == null)
            {
                additionalData = camera.gameObject.AddComponent<AdditionalCameraData>();
                additionalData.hideFlags = HideFlags.HideAndDontSave;
            }

            additionalData.CreatePersistentResources(camera);

            Light pointLight = null;

            Object[] lights = Object.FindObjectsByType(typeof(Light), FindObjectsSortMode.None);

            foreach (Object l in lights)
            {
                Light light = (Light)l;
                if (light != null) 
                {
                    if (light.type == LightType.Point) 
                    {
                        pointLight = light;
                        break;
                    }
                }
            }

            if (pointLight == null)
                return;

            RTHandle outputRTHandle = rtHandleSystem.Alloc(additionalData.rayTracingOutput, "g_Radiance");

            CommandBuffer commandBuffer = new CommandBuffer();

            bool buildRTASForCamera = true;

            if (camera.cameraType == CameraType.Game || camera.cameraType == CameraType.SceneView)
            {
                context.SetupCameraProperties(camera);

                var renderGraphParams = new RenderGraphParameters()
                {
                    scriptableRenderContext = context,
                    commandBuffer = commandBuffer,
                    currentFrameIndex = additionalData.frameIndex
                };

                using (renderGraph.RecordAndExecute(renderGraphParams))
                {
                    RayTracingAccelerationStructureHandle rtasHandle = renderGraph.ImportRayTracingAccelerationStructure(rtas, "g_AccelStruct");

                    RTASBuildPassData rtasBuildPassData = new RTASBuildPassData();

                    if (buildRTASForCamera)
                    {
                        // Build the RTAS only for the first camera in the list, unless we do camera relative ray tracing by specifying a camera position in BuildRayTracingAccelerationStructure.
                        buildRTASForCamera = false;

                        var passName = renderPipelineAsset.useAsyncRTASBuild ? "RTAS Build Pass (Async Compute)" : "RTAS Build Pass";
                        using (var builder = renderGraph.AddRenderPass<RTASBuildPassData>(passName, out rtasBuildPassData))
                        {
                            builder.EnableAsyncCompute(renderPipelineAsset.useAsyncRTASBuild);

                            rtasBuildPassData.rtasHandleOutput = builder.WriteRayTracingAccelerationStructure(rtasHandle);

                            builder.SetRenderFunc(
                                (RTASBuildPassData data, RenderGraphContext ctx) =>
                                {
                                    ctx.cmd.BuildRayTracingAccelerationStructure(data.rtasHandleOutput);
                                });
                        }
                    }                    

                    using (var builder = renderGraph.AddRenderPass<RayTracingRenderPassData>("My RayTracing Pass", out var passData))
                    {
                        TextureHandle output = renderGraph.ImportTexture(outputRTHandle);

                        passData.outputTexture = builder.WriteTexture(output);

                        passData.rtasHandleInput = builder.ReadRayTracingAccelerationStructure(rtasBuildPassData.rtasHandleOutput);

                        builder.SetRenderFunc(
                            (RayTracingRenderPassData data, RenderGraphContext ctx) =>
                            {
                                ctx.cmd.SetRayTracingShaderPass(renderPipelineAsset.rayTracingShader, "Test");

                                // Input
                                ctx.cmd.SetGlobalVector(Shader.PropertyToID("PointLightPosition"), pointLight.transform.position);
                                ctx.cmd.SetGlobalVector(Shader.PropertyToID("PointLightColor"), pointLight.color);
                                ctx.cmd.SetGlobalFloat(Shader.PropertyToID("PointLightRange"), pointLight.range);
                                ctx.cmd.SetGlobalFloat(Shader.PropertyToID("PointLightIntensity"), pointLight.intensity);
                                ctx.cmd.SetGlobalRayTracingAccelerationStructure(Shader.PropertyToID("g_SceneAccelStruct"), passData.rtasHandleInput);

                                ctx.cmd.SetRayTracingMatrixParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_InvViewMatrix"), camera.cameraToWorldMatrix);                                
                                ctx.cmd.SetRayTracingFloatParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_Zoom"), Mathf.Tan(Mathf.Deg2Rad * camera.fieldOfView * 0.5f));
                                ctx.cmd.SetRayTracingFloatParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_AspectRatio"), camera.pixelWidth / (float)camera.pixelHeight);
                                ctx.cmd.SetRayTracingTextureParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_EnvTex"), renderPipelineAsset.envTexture);

                                // Output
                                ctx.cmd.SetRayTracingTextureParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_Output"), passData.outputTexture);

                                ctx.cmd.DispatchRays(renderPipelineAsset.rayTracingShader, "MainRayGenShader", (uint)camera.pixelWidth, (uint)camera.pixelHeight, 1, camera);
                            }
                            );
                    }
                }

                commandBuffer.Blit(additionalData.rayTracingOutput, camera.activeTexture);

                outputRTHandle.Release();
            }
            else if (camera.cameraType == CameraType.Preview)
            {
                commandBuffer.ClearRenderTarget(false, true, Color.magenta);
            }

            context.ExecuteCommandBuffer(commandBuffer);

            commandBuffer.Release();

            context.Submit();

            rtHandleSystem.Release(outputRTHandle);

            renderGraph.EndFrame();

            additionalData.UpdateCameraDataPostRender(camera);
        }        
    }
}
