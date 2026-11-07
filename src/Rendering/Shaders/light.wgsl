// The light drawables are shaded with, shared by surface.wgsl, ground_shadow.wgsl and tonemap.wgsl: the key light
// where it reaches the ground, its shadow map, and the sky's irradiance as order-2 spherical harmonics (sky_luts.wgsl).
// Put after scene.wgsl, as group 2.

struct Light {
    // Unit direction towards the key light, and the exposure multiplier (2^EV) in w.
    sun_direction: vec4f,
    // The key light's illuminance at the ground, after the atmosphere: top-of-atmosphere times transmittance.
    sun_illuminance: vec4f,
    // From origin-relative positions to the shadow map's clip space.
    shadow: mat4x4f,
    // The shadow map's texel in metres, its depth range in metres, its resolution, and whether shadows are on.
    shadow_parameters: vec4f,
};

@group(2) @binding(0) var<uniform> light: Light;
@group(2) @binding(1) var<storage, read> sky_irradiance: array<vec4f, 9>;
@group(2) @binding(2) var shadow_map: texture_depth_2d;
@group(2) @binding(3) var shadow_compare: sampler_comparison;

// Irradiance over pi from the sky's harmonics (RedFury's sky_irradiance), for a unit normal: the Lambert scale that
// multiplies albedo.
fn sky_ambient(n: vec3f) -> vec3f {
    let h = sky_irradiance;
    let ambient = h[0].rgb * 0.282095
        + h[1].rgb * 0.488603 * n.y + h[2].rgb * 0.488603 * n.z + h[3].rgb * 0.488603 * n.x
        + h[4].rgb * 1.092548 * n.x * n.y + h[5].rgb * 1.092548 * n.y * n.z
        + h[6].rgb * 0.315392 * (3.0 * n.z * n.z - 1.0)
        + h[7].rgb * 1.092548 * n.x * n.z + h[8].rgb * 0.546274 * (n.x * n.x - n.y * n.y);
    return max(ambient, vec3f(0.0));
}

// The key light's shadow at a point, as RedFury's cascade_pcf for its one map: nine bilinear comparison taps one
// texel apart (a smooth 4x4 tent), offset along the normal on the side facing the sun by the texel's footprint, which
// keeps grazing surfaces and both-sided cards from shadowing themselves in stripes.
fn sun_shadow(p: vec3f, n: vec3f) -> f32 {
    if (light.shadow_parameters.w < 0.5) {
        return 1.0;
    }
    let texel_world = light.shadow_parameters.x;
    let sun = light.sun_direction.xyz;
    let facing = dot(n, sun);
    let towards_sun = select(-n, n, facing >= 0.0);
    let offset = p + towards_sun * texel_world * (0.75 + 1.75 * (1.0 - abs(facing))) + sun * texel_world * 0.5;
    let clip = light.shadow * vec4f(offset, 1.0);
    let uvz = vec3f(clip.x * 0.5 + 0.5, 0.5 - clip.y * 0.5, clip.z);
    if (any(uvz.xy <= vec2f(0.0)) || any(uvz.xy >= vec2f(1.0)) || uvz.z >= 1.0 || uvz.z <= 0.0) {
        return 1.0;
    }
    let texel = 1.0 / light.shadow_parameters.z;
    let reference = uvz.z - texel_world / light.shadow_parameters.y;
    var sum = 0.0;
    for (var y = -1; y <= 1; y++) {
        for (var x = -1; x <= 1; x++) {
            sum += textureSampleCompareLevel(shadow_map, shadow_compare, uvz.xy + vec2f(f32(x), f32(y)) * texel, reference);
        }
    }
    return sum / 9.0;
}
