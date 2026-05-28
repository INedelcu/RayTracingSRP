using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RendererUtils;

public class RayTracingRenderPipelineInstance : RenderPipeline
{
    private RayTracingRenderPipelineAsset renderPipelineAsset;

    private RayTracingAccelerationStructure rtas = null;

    private UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph = null;

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

        renderGraph = new UnityEngine.Rendering.RenderGraphModule.RenderGraph("THE Render Graph");

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

    class RayTracingRenderPassData
    {
        public UnityEngine.Rendering.RenderGraphModule.TextureHandle outputTexture;
    };

    protected override void Render (ScriptableRenderContext context, List<Camera> cameras)
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

        try
        {
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

            UnityEngine.Object[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);

            foreach (UnityEngine.Object l in lights)
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
            {
                return;
            }

            CommandBuffer commandBuffer = new CommandBuffer();

            bool buildRTASForCamera = true;

            if (camera.cameraType == CameraType.Game || camera.cameraType == CameraType.SceneView)
            {
                context.SetupCameraProperties(camera);

                var renderGraphParams = new RenderGraphParameters()
                {
                    scriptableRenderContext = context,
                    commandBuffer = commandBuffer,
                    currentFrameIndex = additionalData.frameIndex,
                    executionId = camera.GetEntityId(),
                    generateDebugData = true
                };

                RTHandle outputRTHandle = rtHandleSystem.Alloc(additionalData.rayTracingOutput, "g_Output");

                renderGraph.BeginRecording(renderGraphParams);
                {
                    using (var builder = renderGraph.AddUnsafePass<RayTracingRenderPassData>("My RayTracing Pass", out var passData))
                    {
                        RenderTargetInfo renderTagetInfo = new RenderTargetInfo()
                        {
                            width = additionalData.rayTracingOutput.width,
                            height = additionalData.rayTracingOutput.height,
                            bindMS = false,
                            format = additionalData.rayTracingOutput.graphicsFormat,
                            msaaSamples = 1,
                            volumeDepth = 1
                        };

                        TextureHandle output = renderGraph.ImportTexture(outputRTHandle, renderTagetInfo);

                        passData.outputTexture = output;

                        builder.UseTexture(passData.outputTexture, AccessFlags.Write);
                        builder.AllowPassCulling(false);

                        builder.SetRenderFunc(
                            (RayTracingRenderPassData data, UnsafeGraphContext ctx) =>
                            {
                                CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);

                                if (buildRTASForCamera)
                                {
                                    // Build the RTAS only for one camera.
                                    buildRTASForCamera = false;

                                    cmd.BuildRayTracingAccelerationStructure(rtas);
                                }

                                cmd.SetRayTracingShaderPass(renderPipelineAsset.rayTracingShader, "Test");

                                // Input
                                cmd.SetGlobalVector(Shader.PropertyToID("PointLightPosition"), pointLight.transform.position);
                                cmd.SetGlobalVector(Shader.PropertyToID("PointLightColor"), pointLight.color);
                                cmd.SetGlobalFloat(Shader.PropertyToID("PointLightRange"), pointLight.range);
                                cmd.SetGlobalFloat(Shader.PropertyToID("PointLightIntensity"), pointLight.intensity);
                                cmd.SetRayTracingAccelerationStructure(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_SceneAccelStruct"), rtas);

                                cmd.SetRayTracingMatrixParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_InvViewMatrix"), camera.cameraToWorldMatrix);
                                cmd.SetRayTracingFloatParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_Zoom"), Mathf.Tan(Mathf.Deg2Rad * camera.fieldOfView * 0.5f));
                                cmd.SetRayTracingFloatParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_AspectRatio"), camera.pixelWidth / (float)camera.pixelHeight);
                                cmd.SetRayTracingTextureParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_EnvTex"), renderPipelineAsset.envTexture);

                                // Output
                                cmd.SetRayTracingTextureParam(renderPipelineAsset.rayTracingShader, Shader.PropertyToID("g_Output"), passData.outputTexture);

                                cmd.DispatchRays(renderPipelineAsset.rayTracingShader, "MainRayGenShader", (uint)camera.pixelWidth, (uint)camera.pixelHeight, 1, camera);
                            }
                            );
                    }
                }

                renderGraph.EndRecordingAndExecute();

                commandBuffer.Blit(additionalData.rayTracingOutput, camera.activeTexture);

                outputRTHandle.Release();
            }
            else if (camera.cameraType == CameraType.Preview)
            {
                context.SetupCameraProperties(camera);

                if (camera.TryGetCullingParameters(out var cullingParameters))
                {
                    var cullingResults = context.Cull(ref cullingParameters);

                    bool clearDepth = camera.clearFlags != CameraClearFlags.Nothing;
                    bool clearColor = camera.clearFlags == CameraClearFlags.SolidColor;
                    commandBuffer.ClearRenderTarget(clearDepth, clearColor, camera.backgroundColor.linear);

                    var shaderTagIds = new ShaderTagId[]
                    {
                        new ShaderTagId("SRPDefaultUnlit"),
                        new ShaderTagId("ForwardBase"),
                    };

                    var opaqueDesc = new RendererListDesc(shaderTagIds, cullingResults, camera)
                    {
                        sortingCriteria = SortingCriteria.CommonOpaque,
                        renderQueueRange = RenderQueueRange.opaque,
                    };
                    commandBuffer.DrawRendererList(context.CreateRendererList(opaqueDesc));

                    if (camera.clearFlags == CameraClearFlags.Skybox && RenderSettings.skybox != null)
                    {
                        commandBuffer.DrawRendererList(context.CreateSkyboxRendererList(camera));
                    }

                    var transparentDesc = new RendererListDesc(shaderTagIds, cullingResults, camera)
                    {
                        sortingCriteria = SortingCriteria.CommonTransparent,
                        renderQueueRange = RenderQueueRange.transparent,
                    };
                    commandBuffer.DrawRendererList(context.CreateRendererList(transparentDesc));
                }
            }

            context.ExecuteCommandBuffer(commandBuffer);

            commandBuffer.Release();

            context.Submit();

            additionalData.UpdateCameraDataPostRender(camera);
        }
        }
        catch (Exception e)
        {
            if (renderGraph.ResetGraphAndLogException(e))
            {
                throw;
            }
        }

        renderGraph.EndFrame();
    }
}
