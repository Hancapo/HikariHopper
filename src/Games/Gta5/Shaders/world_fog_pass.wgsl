// GTA V's fog over its world, as the game's post-process draws it once the world's solid surfaces and decals are
// drawn (the same arithmetic in Enhanced's shaders):
//   - The fog rays (pp_fogray, PS_FogRays): at half the frame's size, how much of each pixel's ray from the camera the
//     sun reaches, eight shadow samples along it, each pixel of a three by three cell
//     starting a ninth of a step further, jittered past 800 metres; kept with its depth along the view.
//   - Blurred across and then down (pp_GaussBlurBilateral_Hor and _Ver, 9 by 9 in three bilinear taps a side), each tap
//     weighed by how near its depth is.
//   - The fog itself (pp_depthfx, PS_DepthEffects): a triangle over the frame takes each pixel's depth back to its
//     point and blends the fog along the ray to it (Gta5/fog.wgsl, with the haze) over it, by the fog's amount, into
//     colour only; its colour taken by the fog rays, from the four map pixels round it, the one of nearest depth
//     unless all are near.
// The sky, which nothing covers, fogs itself (Gta5/sky.wgsl); the water and blended surfaces drawn after this fog
// themselves (Gta5/world_fog.wgsl), the water by its own surface, as the game's pass would over it.
//   - The game skips what's nearer than the fog's start with a depth test against a plane there; nothing there has any
//     fog, so here it's simply drawn over. Its fog rays read a smaller copy of its shadow map; here the shadow's own
//     cascades.
// Put after Scene/scene.wgsl, Scene/light.wgsl, Gta5/fog.wgsl and Gta5/world_fog.wgsl.

// The world's depth (reversed Z, infinitely far), read where it's attached.
@group(1) @binding(0) var fog_depth: texture_depth_2d;

// The fog rays' map being blurred, and how it's read.
@group(1) @binding(0) var blur_source: texture_2d<f32>;
@group(1) @binding(1) var blur_sampler: sampler;

// Where a sky's depth is kept in the map: past any fog ray's reach (a half float's range).
const FOG_RAY_FAR: f32 = 65000.0;
// PS_FogRays' samples along each ray.
const FOG_RAY_SAMPLES: i32 = 8;
// Where the rays' starts are jittered over.
const FOG_RAY_JITTER_START: f32 = 800.0;
const FOG_RAY_JITTER_END: f32 = 1100.0;

@vertex
fn vs_gta_world_fog(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
    let corner = vec2f(f32((index << 1u) & 2u), f32(index & 2u));
    return vec4f(corner * 2.0 - 1.0, 0.0, 1.0);
}

// The ray through a point of the frame (0 to 1 across, down), one metre along the view.
fn fog_view_ray(uv: vec2f) -> vec3f {
    let ndc = vec2f(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    let tan_half = scene.projection.y;
    return scene.forward.xyz + scene.right.xyz * (ndc.x * tan_half * scene.projection.x) + scene.up.xyz * (ndc.y * tan_half);
}

// A random float, nought to one.
fn fog_ray_random(uv: vec2f) -> f32 {
    let noise = fract(sin(dot(uv, vec2f(12.9898, 78.233) * 2.0)) * 43758.5453);
    return abs(noise + noise) - 1.0;
}

// How much of the sun reaches the points from p, a step of d apart, each in the first
// cascade it's inside (short of a filter's width of its edge), lit where it's in none; p and d relative to the scene's
// origin.
fn fog_ray_shadow(p: vec3f, d: vec3f) -> f32 {
    let range = 1.0 - 1.5 * 2.0 / light.shadow_parameters.z;
    var at = array<vec4f, 4>(light.shadow[0] * vec4f(p, 1.0), light.shadow[1] * vec4f(p, 1.0), light.shadow[2] * vec4f(p, 1.0),
        light.shadow[3] * vec4f(p, 1.0));
    let step = array<vec4f, 4>(light.shadow[0] * vec4f(d, 0.0), light.shadow[1] * vec4f(d, 0.0), light.shadow[2] * vec4f(d, 0.0),
        light.shadow[3] * vec4f(d, 0.0));
    var sum = 0.0;
    for (var i = 0; i < FOG_RAY_SAMPLES; i++) {
        var lit = 1.0;
        for (var c = 0; c < 4; c++) {
            let clip = at[c];
            if (max(abs(clip.x), abs(clip.y)) < range) {
                if (clip.z > 0.0 && clip.z < 1.0) {
                    let uv = vec2f(clip.x * 0.5 + 0.5, 0.5 - clip.y * 0.5);
                    lit = textureSampleCompareLevel(shadow_map, shadow_compare, uv, c, clip.z);
                }
                break;
            }
        }
        sum += lit;
        for (var c = 0; c < 4; c++) {
            at[c] += step[c];
        }
    }
    return sum / f32(FOG_RAY_SAMPLES);
}

@fragment
fn fs_gta_fog_rays(@builtin(position) position: vec4f) -> @location(0) vec4f {
    // The farthest of the four depths under the pixel.
    let full = vec2i(textureDimensions(fog_depth));
    let corner = vec2i(position.xy) * 2;
    let limit = full - vec2i(1);
    let z = min(min(textureLoad(fog_depth, min(corner, limit), 0), textureLoad(fog_depth, min(corner + vec2i(1, 0), limit), 0)),
        min(textureLoad(fog_depth, min(corner + vec2i(0, 1), limit), 0), textureLoad(fog_depth, min(corner + vec2i(1, 1), limit), 0)));
    let uv = position.xy / vec2f((full + vec2i(1)) / 2);
    let depth = select(FOG_RAY_FAR, min(light.water.z / z, FOG_RAY_FAR), z > 0.0);
    let to_point = fog_view_ray(uv) * depth;
    let eye_distance = length(to_point);
    let jitter = mix(0.0, 0.5 * fog_ray_random(uv),
        saturate((eye_distance - FOG_RAY_JITTER_START) / (FOG_RAY_JITTER_END - FOG_RAY_JITTER_START)));
    // Each pixel of a three by three cell starts a ninth of a step further along.
    let cell = position.xy - floor(position.xy / 3.0) * 3.0;
    let offset = cell.x + cell.y * 3.0;
    let delta = to_point / f32(FOG_RAY_SAMPLES + 1);
    let start = scene.eye.xyz - scene.origin.xyz + delta * offset / 9.0 + jitter * delta;
    var ray = fog_ray_shadow(start, delta);
    // Faded out towards the shadow's reach, over the last cascade's, by the timecycle's fades (only the fade's length
    // counts, as the game's shader takes it).
    let last = light.shadow_splits.w - light.shadow_splits.z;
    let fade = saturate(eye_distance / max(last * (world_fog.rays.z - world_fog.rays.y), 1e-3));
    ray = mix(ray, 1.0, fade);
    return vec4f(ray, depth, 0.0, 0.0);
}

// PS_GaussBlur9x9Bilateral_H and _V: the map blurred one way, by depth.
fn fog_ray_blur(position: vec2f, axis: vec2f) -> vec4f {
    let texel = 1.0 / vec2f(textureDimensions(blur_source));
    let uv = position * texel;
    let centre = textureSampleLevel(blur_source, blur_sampler, uv, 0.0).xy;
    let offsets = array<f32, 3>(0.0, 1.38462, 3.23077);
    let weights = array<f32, 3>(0.22703, 0.31622, 0.07027);
    var sum = centre.x * weights[0];
    var total = weights[0];
    for (var i = 1; i < 3; i++) {
        let a = textureSampleLevel(blur_source, blur_sampler, uv + axis * offsets[i] * texel, 0.0).xy;
        let b = textureSampleLevel(blur_source, blur_sampler, uv - axis * offsets[i] * texel, 0.0).xy;
        let wa = exp(-10.0 * abs(a.y - centre.y) / centre.y) * weights[i];
        let wb = exp(-10.0 * abs(b.y - centre.y) / centre.y) * weights[i];
        sum += a.x * wa + b.x * wb;
        total += wa + wb;
    }
    return vec4f(sum / total, centre.y, 0.0, 0.0);
}

@fragment
fn fs_gta_fog_rays_across(@builtin(position) position: vec4f) -> @location(0) vec4f {
    return fog_ray_blur(position.xy, vec2f(1.0, 0.0));
}

@fragment
fn fs_gta_fog_rays_down(@builtin(position) position: vec4f) -> @location(0) vec4f {
    return fog_ray_blur(position.xy, vec2f(0.0, 1.0));
}

// The depth effects' fog rays: the four map pixels round this one, the one of nearest depth, or their mean where all
// are within a hundredth of it.
fn fog_ray_upsampled(position: vec2f, size: vec2f, depth: f32) -> f32 {
    let low = size * 0.5;
    let centre = round(position / size * low) / low;
    let rays = textureGather(0, fog_ray_map, reflection_sampler, centre);
    let depths = textureGather(1, fog_ray_map, reflection_sampler, centre);
    let differences = select(vec4f(0.0), abs((depths - depth) / depths), depths > vec4f(0.0));
    var best = vec2f(rays.x, differences.x);
    if (differences.y < best.y) {
        best = vec2f(rays.y, differences.y);
    }
    if (differences.z < best.y) {
        best = vec2f(rays.z, differences.z);
    }
    if (differences.w < best.y) {
        best = vec2f(rays.w, differences.w);
    }
    return select(best.x, dot(rays, vec4f(0.25)), all(differences < vec4f(0.01)));
}

@fragment
fn fs_gta_world_fog(@builtin(position) position: vec4f) -> @location(0) vec4f {
    let z = textureLoad(fog_depth, vec2i(position.xy), 0);
    // The sky: nothing was drawn there.
    if (z == 0.0) {
        discard;
    }
    // The pixel's point: along its ray, as far along the view as the depth says (the near plane over it).
    let size = vec2f(textureDimensions(fog_depth));
    let depth = light.water.z / z;
    var fog = gta_fog(world_fog.fog, fog_view_ray(position.xy / size) * depth, 1.0);
    let strength = gta_fog_ray_strength();
    if (strength > 0.0) {
        fog = vec4f(fog.rgb * saturate(mix(1.0, fog_ray_upsampled(position.xy, size, depth), strength)), fog.a);
    }
    return fog;
}
