// The reflection map drawables reflect, laid out as GTA V's is: a dual paraboloid, the upper hemisphere on the right
// half and the lower on the left, which PS_directional_ambient samples at a mip chosen by gloss (light.wgsl). The
// game renders its world and sky into it; the viewer has only the sky (sky_system.wgsl, brightened as the game's
// reflection pass is) and a stand-in ground below the horizon. Each mip is the one above it averaged two by two.
// Put after sky_system.wgsl.

@group(0) @binding(0) var<uniform> sky: SkySystem;
@group(1) @binding(0) var source: texture_2d<f32>;
@group(1) @binding(1) var source_sampler: sampler;

@vertex
fn vs_reflection_map(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
    let corner = vec2f(f32((index << 1u) & 2u), f32(index & 2u));
    return vec4f(corner * 2.0 - 1.0, 0.0, 1.0);
}

// The direction a texel of the map holds: the inverse of light.wgsl's paraboloid_uv. Texels outside a hemisphere's
// disc take its rim, so filtering and mips near the rim stay clean.
fn paraboloid_direction(uv: vec2f) -> vec3f {
    let upper = uv.x >= 0.5;
    var p = vec2f(select((0.25 - uv.x) * 4.0, (uv.x - 0.75) * 4.0, upper), (0.5 - uv.y) * 2.0);
    let r2 = dot(p, p);
    if (r2 > 1.0) {
        p = p / sqrt(r2);
    }
    let q = dot(p, p);
    let z = (1.0 - q) / (1.0 + q);
    return normalize(vec3f(2.0 * p / (1.0 + q), select(-z, z, upper)));
}

// The map's top mip, as ViewportRenderer.Reflections.cs makes it: each hemisphere 256 by 256.
const REFLECTION_MAP_SIZE: vec2f = vec2f(512.0, 256.0);

@fragment
fn fs_reflection_map(@builtin(position) frag: vec4f) -> @location(0) vec4f {
    let d = paraboloid_direction(frag.xy / REFLECTION_MAP_SIZE);
    // Below the horizon the game reflects its world; the viewer fades to its ground there.
    let ground = smoothstep(0.0, -0.04, d.z);
    return vec4f(mix(sky_system(sky, d), sky.ground.rgb, ground), 1.0);
}

@fragment
fn fs_reflection_downsample(@builtin(position) frag: vec4f) -> @location(0) vec4f {
    // Halfway between four texels of the mip above: one bilinear tap is their average.
    let size = vec2f(textureDimensions(source, 0));
    return textureSampleLevel(source, source_sampler, frag.xy * 2.0 / size, 0.0);
}
