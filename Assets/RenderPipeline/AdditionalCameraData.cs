using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public class AdditionalCameraData : MonoBehaviour
{
    [HideInInspector]
    public int frameIndex;

    [HideInInspector]
    public RenderTexture rayTracingOutput = null;

    // Start is called before the first frame update
    void Start()
    {
        frameIndex = 0;
    }

    // Update is called once per frame
    void Update()
    {
    }

    public void UpdateCameraDataPostRender(Camera camera)
    {
        frameIndex++;
    }

    public void CreatePersistentResources(Camera camera)
    {
        if (rayTracingOutput == null || rayTracingOutput.width != camera.pixelWidth || rayTracingOutput.height != camera.pixelHeight)
        {
            if (rayTracingOutput)
                rayTracingOutput.Release();

            RenderTextureDescriptor rtDesc = new RenderTextureDescriptor()
            {
                dimension = TextureDimension.Tex2D,
                width = camera.pixelWidth,
                height = camera.pixelHeight,
                depthBufferBits = 0,
                volumeDepth = 1,
                msaaSamples = 1,
                vrUsage = VRTextureUsage.OneEye,
                graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                enableRandomWrite = true,
            };

            rayTracingOutput = new RenderTexture(rtDesc);
            rayTracingOutput.Create();
        }
    }

    void OnDestroy()
    {
        if (rayTracingOutput != null)
        {
            rayTracingOutput.Release();
            rayTracingOutput = null;
        }
    }
}
