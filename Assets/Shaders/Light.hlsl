cbuffer PointLight : register(b0, space1)
{ 
	float3 PointLightPosition;
	float3 PointLightColor;
    float PointLightRange;
    float PointLightIntensity;
};

float CalculateLightFalloff(float distance, float lightRadius)
{
    return 1 - saturate(distance / lightRadius);
}