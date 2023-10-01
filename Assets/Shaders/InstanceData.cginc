#pragma once

#define E_PI 3.1415926535897932384626433832795028841971693993751058209749445923078164062

struct BBox
{
    float3 center;
    float3 extents;
};

struct GrassInstanceData
{
    float4x4 mat;
    float4 color;
};

uint hash( uint x ) {
    x += ( x << 10u );
    x ^= ( x >>  6u );
    x += ( x <<  3u );
    x ^= ( x >> 11u );
    x += ( x << 15u );
    return x;

}

uint hash( uint2 v ) { return hash( v.x ^ hash(v.y)                         ); }
uint hash( uint3 v ) { return hash( v.x ^ hash(v.y) ^ hash(v.z)             ); }
uint hash( uint4 v ) { return hash( v.x ^ hash(v.y) ^ hash(v.z) ^ hash(v.w) ); }

float floatConstruct( uint m ) {

    const uint ieeeMantissa = 0x007FFFFFu;
    const uint ieeeOne      = 0x3F800000u;

    m &= ieeeMantissa;                    
    m |= ieeeOne;                         

    float  f = asfloat(m);      
    return f - 1.0;                       
}

float rand(uint instanceId)
{
    return floatConstruct(hash(instanceId));
}

float rand(uint3 DTid)
{
    return floatConstruct(hash(DTid));
}