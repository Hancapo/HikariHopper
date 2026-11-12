// The reflection map drawables reflect, laid out as GTA V's is: a dual paraboloid, the upper hemisphere on the right
// half and the lower on the left, which PS_directional_ambient samples at a mip chosen by gloss (light.wgsl). The
// game renders its world and sky into it; the viewer has only the sky (sky_system.wgsl, brightened as the game's
// reflection pass is) and, below the horizon, a stand-in ground in the sky map and the backdrop's floor (its colour
// and grid, from the camera) in the map drawables reflect. Each mip is the one above it averaged two by two.
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

// The backdrop's floor as the scene map reflects it, from the camera as the backdrop draws it (backdrop.wgsl).
struct ReflectionFloor {
    // The camera and the floor's height, in the world, so the grid lies where the backdrop's does.
    eye: vec4f,
    // The grid's minor spacing; how bright the scene's light makes an albedo of one, the timecycle's ground radiance
    // over a grey card's 0.18.
    grid: vec4f,
    // The backdrop's colours, linear: below the horizon, and the minor and major lines with their opacity.
    void_colour: vec4f,
    minor: vec4f,
    major: vec4f,
};

@group(1) @binding(0) var<uniform> reflection_floor: ReflectionFloor;

// Coverage of the nearest line of a unit grid, about one texel wide, as backdrop.wgsl's.
fn floor_line(coordinate: vec2f, derivative: vec2f) -> f32 {
    let distance_to_line = abs(fract(coordinate - 0.5) - 0.5) / max(derivative, vec2f(0.00001));
    return 1.0 - clamp(min(distance_to_line.x, distance_to_line.y), 0.0, 1.0);
}

// The floor below the horizon, blended over the sky the scene map starts as: the backdrop's colours taken as albedos
// under the scene's light, so what the view shows under a model is what its paint and glass mirror.
@fragment
fn fs_reflection_floor(@builtin(position) frag: vec4f) -> @location(0) vec4f {
    let d = paraboloid_direction(frag.xy / REFLECTION_MAP_SIZE);
    let f = reflection_floor;
    // Where the direction meets the floor; derivatives need uniform control flow, so this runs for every texel.
    let toward = select(d.z, -1e-6, abs(d.z) < 1e-6);
    let distance = (f.eye.w - f.eye.z) / toward;
    let point = f.eye.xyz + d * distance;
    let cell = point.xy / f.grid.x;
    let minor = floor_line(cell, fwidth(cell));
    let major = floor_line(cell * 0.1, fwidth(cell * 0.1));
    var colour = f.void_colour.rgb;
    if (distance > 0.0) {
        // Faded with distance and at grazing angles, as the backdrop's grid is.
        let fade = exp(-distance / (f.grid.x * 400.0)) * smoothstep(0.0, 0.08, abs(d.z));
        colour = mix(colour, f.minor.rgb, minor * f.minor.a * fade);
        colour = mix(colour, f.major.rgb, major * f.major.a * fade);
    }
    return vec4f(colour * f.grid.y, smoothstep(0.0, -0.01, d.z));
}

@fragment
fn fs_reflection_downsample(@builtin(position) frag: vec4f) -> @location(0) vec4f {
    // Halfway between four texels of the mip above: one bilinear tap is their average.
    let size = vec2f(textureDimensions(source, 0));
    return textureSampleLevel(source, source_sampler, frag.xy * 2.0 / size, 0.0);
}
