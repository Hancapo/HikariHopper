// The light drawables are shaded with, shared by surface.wgsl, ground_shadow.wgsl and tonemap.wgsl: GTA V's
// timecycle light at the viewer's time of day (Timecycle.cs), the exposure and filmic curve its post-process settles
// on, the directional light's shadow map, and the reflection map (reflection_map.wgsl). Put after scene.wgsl, as
// group 2.

struct Light {
    // Unit direction towards the directional light (the sun, or the moon), and the exposure multiplier (2^stops) in w.
    sun_direction: vec4f,
    // The directional light's colour times its multiplier, as the directional pass reads it.
    sun_colour: vec4f,
    // The natural ambient's down light (from the sky, on what faces up), with the down wrap in w.
    natural_down: vec4f,
    // The natural ambient's up light (from the ground, on what faces down), with the props' occlusion multiplier in w.
    natural_up: vec4f,
    // The directional ambient's colour times its intensity, with the vehicles' occlusion multiplier in w.
    directional_ambient: vec4f,
    // The way the directional ambient comes from, a unit vector.
    directional_ambient_direction: vec4f,
    // The filmic curve: A, B, C, D; then E, F, the white point W and one over the curve at W.
    filmic0: vec4f,
    filmic1: vec4f,
    // The reflection map's mip count (gReflectionMipCount), in x.
    reflection: vec4f,
    // From origin-relative positions to the shadow map's clip space.
    shadow: mat4x4f,
    // The shadow map's texel in metres, its depth range in metres, its resolution, and whether shadows are on.
    shadow_parameters: vec4f,
};

@group(2) @binding(0) var<uniform> light: Light;
@group(2) @binding(1) var shadow_map: texture_depth_2d;
@group(2) @binding(2) var shadow_compare: sampler_comparison;
@group(2) @binding(3) var reflection_map: texture_2d<f32>;
@group(2) @binding(4) var reflection_sampler: sampler;

// PS_directional_ambient's natural ambient for a unit normal: the down light on what faces up, the up light on what
// faces down, the down light wrapping past the horizon by the down wrap; and the directional ambient on what faces it.
fn gta_ambient(n: vec3f) -> vec3f {
    let wrap = light.natural_down.w;
    let h = max(0.0, (n.z + wrap) / (1.0 + wrap));
    let natural = mix(light.natural_up.rgb, light.natural_down.rgb, h);
    let directional = light.directional_ambient.rgb * saturate(dot(light.directional_ambient_direction.xyz, n));
    return natural + directional;
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

// Where PS_directional_ambient looks a direction up in the reflection map: a dual paraboloid, the upper hemisphere on
// the right half of the map and the lower on the left.
fn paraboloid_uv(r: vec3f) -> vec2f {
    let p = r.xy / (abs(r.z) + 1.0);
    let u = select(0.25 - 0.25 * p.x, 0.75 + 0.25 * p.x, r.z > 0.0);
    return vec2f(u, 0.5 - 0.5 * p.y);
}

// PS_directional_ambient's reflection: the map in the reflected direction, at a mip chosen by gloss (the specular
// exponent over 1500) with the game's curve, times what the surface reflects at this angle (fresnel times specular
// intensity), and an energy term that runs from 1/pi for dull surfaces to one for glossy ones (the exponent over 563).
// The caller scales it by the ambient scale, as the game does.
fn gta_reflection(normal: vec3f, view: vec3f, exponent: f32, reflected_share: f32) -> vec3f {
    let direction = reflect(-view, normal);
    let rough = 1.0 - saturate(exponent / 1500.0);
    let mips = light.reflection.x;
    let level = select(rough * rough * 5.0 + mips - 5.0, rough * mips - 5.0, rough * mips < mips - 5.0);
    let environment = textureSampleLevel(reflection_map, reflection_sampler, paraboloid_uv(direction), max(level, 0.0)).rgb;
    let energy = 0.31830987 + 0.68169010 * saturate(exponent / 563.0);
    return environment * reflected_share * energy;
}
