struct RayPayload
{
    float4 color;
    float4 worldPos;
    uint bounceIndex;
};

struct RayPayloadShadow
{
    float shadowValue;
};