// Screen-space ambient occlusion over a world's solid surfaces, as RedFury's viewer works it out (its viewer_post.wgsl's
// fs_gtao and fs_ao_denoise), for both games' worlds:
//   - Ground-truth ambient occlusion (Jimenez et al. 2016): per slice, the two horizon angles found from the depth,
//     and the cosine-weighted visible arc integrated round the projected normal; three slices, four steps each way,
//     XeGTAO's falloff over 1.5 m, the visibility raised to 1.5. A 4 by 4 ordered pattern turns the slices and jitters
//     the steps, and a depth-aware 4 by 4 box takes it out again, without temporal history.
//   - Then taken back out of the frame: the world's solid surfaces wrote the ambient they were lit with, the occlusion
//     already baked into it, their normal and their albedo's luminance (light.wgsl's occludable_ambient and
//     occludable_normal). The ambient is made over as RedFury's surfaces take it, the baked and screen-space
//     occlusion combined by min and through Jimenez's multi-bounce for the albedo: what the frame holds less what it
//     would, subtracted (by its coverage) from the HDR target.
// The depth is infinite reversed Z. Put after scene.wgsl.

@group(1) @binding(0) var ao_depth: texture_depth_2d;
@group(1) @binding(1) var ao_normal: texture_2d<f32>;
@group(1) @binding(2) var ao_ambient: texture_2d<f32>;
@group(1) @binding(3) var ao_input: texture_2d<f32>;

struct AmbientOcclusionSettings {
    // The occlusion's radius in metres, the power its visibility is raised to, the most pixels the radius may cover,
    // and the camera's near plane.
    params: vec4f,
};
@group(1) @binding(4) var<uniform> ao_settings: AmbientOcclusionSettings;

const AO_PI: f32 = 3.14159265359;
const AO_HALF_PI: f32 = 1.57079632679;

@vertex
fn vs_ambient_occlusion(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
    var positions = array<vec2f, 3>(vec2f(-1.0, -1.0), vec2f(3.0, -1.0), vec2f(-1.0, 3.0));
    return vec4f(positions[index], 0.0, 1.0);
}

fn ao_size() -> vec2f {
    return vec2f(textureDimensions(ao_depth));
}

fn ao_clamp_pixel(pixel: vec2i) -> vec2i {
    return clamp(pixel, vec2i(0), vec2i(ao_size()) - vec2i(1));
}

// The distance along the view of a depth.
fn ao_view_distance(depth: f32) -> f32 {
    return ao_settings.params.w / max(depth, 1e-9);
}

// A pixel's place in view space: x right, y up, -z forward.
fn ao_view_position(pixel: vec2f, depth: f32) -> vec3f {
    let uv = pixel / ao_size();
    let ndc = vec2f(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    let distance = ao_view_distance(depth);
    let tan_half = scene.projection.y;
    return vec3f(ndc.x * scene.projection.x * tan_half * distance, ndc.y * tan_half * distance, -distance);
}

// A pixel centre's place in view space; w is nought for the sky.
fn ao_load_view_position(pixel: vec2i) -> vec4f {
    let p = ao_clamp_pixel(pixel);
    let depth = textureLoad(ao_depth, p, 0);
    return vec4f(ao_view_position(vec2f(p) + 0.5, depth), select(0.0, 1.0, depth > 0.0));
}

fn ao_world_to_view(v: vec3f) -> vec3f {
    return vec3f(dot(v, scene.right.xyz), dot(v, scene.up.xyz), -dot(v, scene.forward.xyz));
}

// A 4 by 4 ordered pattern in [0, 1), which a 4 by 4 filter takes out exactly.
fn ao_bayer4(pixel: vec2i) -> f32 {
    let p = vec2u(pixel) & vec2u(3u);
    var table = array<f32, 16>(0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);
    return table[p.y * 4u + p.x] / 16.0;
}

@fragment
fn fs_gtao(@builtin(position) frag: vec4f) -> @location(0) vec4f {
    let pixel = vec2i(frag.xy);
    let depth = textureLoad(ao_depth, pixel, 0);
    let normal_sample = textureLoad(ao_normal, pixel, 0);
    if (depth <= 0.0 || normal_sample.w <= 0.0) {
        return vec4f(1.0);
    }
    let p = ao_view_position(frag.xy, depth);
    let n = normalize(ao_world_to_view(normal_sample.xyz));
    let v = normalize(-p);
    let radius = ao_settings.params.x;
    let pixels_per_unit = ao_size().y * 0.5 / (scene.projection.y * -p.z);
    let screen_radius = min(radius * pixels_per_unit, ao_settings.params.z);
    if (screen_radius < 1.5) {
        return vec4f(1.0);
    }
    let falloff_range = 0.615 * radius;
    let falloff_mul = -1.0 / falloff_range;
    let falloff_add = (radius - falloff_range) / falloff_range + 1.0;
    let slice_noise = ao_bayer4(pixel);
    let step_noise = fract(slice_noise * 5.0 + 0.5);
    let min_step = 1.3 / screen_radius;
    let slice_count = 3u;
    let step_count = 4u;
    var visibility = 0.0;
    for (var slice = 0u; slice < slice_count; slice++) {
        let phi = (f32(slice) + slice_noise) * AO_PI / f32(slice_count);
        let direction = vec3f(cos(phi), sin(phi), 0.0);
        // Screen y points down while view y points up.
        let omega = vec2f(direction.x, -direction.y) * screen_radius;
        let ortho = direction - dot(direction, v) * v;
        let axis = normalize(cross(ortho, v));
        let projected = n - axis * dot(n, axis);
        let projected_length = length(projected);
        let cos_n = clamp(dot(projected, v) / max(projected_length, 1e-5), 0.0, 1.0);
        let angle_n = sign(dot(ortho, projected)) * acos(cos_n);
        let low0 = cos(angle_n + AO_HALF_PI);
        let low1 = cos(angle_n - AO_HALF_PI);
        var horizon0 = low0;
        var horizon1 = low1;
        for (var k = 0u; k < step_count; k++) {
            var t = (f32(k) + step_noise) / f32(step_count);
            t = t * t + min_step;
            let offset = omega * t;
            let s0 = ao_load_view_position(vec2i(frag.xy + offset));
            let s1 = ao_load_view_position(vec2i(frag.xy - offset));
            let d0 = s0.xyz - p;
            let d1 = s1.xyz - p;
            let l0 = max(length(d0), 1e-6);
            let l1 = max(length(d1), 1e-6);
            let w0 = clamp(l0 * falloff_mul + falloff_add, 0.0, 1.0) * s0.w;
            let w1 = clamp(l1 * falloff_mul + falloff_add, 0.0, 1.0) * s1.w;
            horizon0 = max(horizon0, mix(low0, dot(d0 / l0, v), w0));
            horizon1 = max(horizon1, mix(low1, dot(d1 / l1, v), w1));
        }
        var h0 = -acos(clamp(horizon1, -1.0, 1.0));
        var h1 = acos(clamp(horizon0, -1.0, 1.0));
        h0 = angle_n + clamp(h0 - angle_n, -AO_HALF_PI, AO_HALF_PI);
        h1 = angle_n + clamp(h1 - angle_n, -AO_HALF_PI, AO_HALF_PI);
        let sin_n = sin(angle_n);
        let arc0 = (cos_n + 2.0 * h0 * sin_n - cos(2.0 * h0 - angle_n)) * 0.25;
        let arc1 = (cos_n + 2.0 * h1 * sin_n - cos(2.0 * h1 - angle_n)) * 0.25;
        visibility += projected_length * (arc0 + arc1);
    }
    visibility = pow(clamp(visibility / f32(slice_count), 0.0, 1.0), ao_settings.params.y);
    return vec4f(max(visibility, 0.03));
}

// A depth-aware 4 by 4 box: one sample of each cell of the pattern, so it cancels out.
@fragment
fn fs_ao_denoise(@builtin(position) frag: vec4f) -> @location(0) vec4f {
    let pixel = vec2i(frag.xy);
    let depth = textureLoad(ao_depth, pixel, 0);
    if (depth <= 0.0) {
        return vec4f(1.0);
    }
    let distance = ao_view_distance(depth);
    let tolerance = distance * 0.08 + 1e-4;
    var sum = 0.0;
    var weight = 0.0;
    for (var y = -2; y <= 1; y++) {
        for (var x = -2; x <= 1; x++) {
            let q = ao_clamp_pixel(pixel + vec2i(x, y));
            let neighbour = ao_view_distance(textureLoad(ao_depth, q, 0));
            let w = clamp(1.0 - abs(neighbour - distance) / tolerance, 0.0, 1.0);
            sum += textureLoad(ao_input, q, 0).r * w;
            weight += w;
        }
    }
    return vec4f(sum / max(weight, 1e-4));
}

// Multi-bounce approximation for ambient occlusion (Jimenez et al. 2016): brighter albedo recovers light a
// single-bounce visibility would lose.
fn ao_multi_bounce(visibility: f32, albedo: vec3f) -> vec3f {
    let a = 2.0404 * albedo - 0.3324;
    let b = -4.7951 * albedo + 0.6417;
    let c = 2.7552 * albedo + 0.6903;
    return max(vec3f(visibility), ((visibility * a + b) * visibility + c) * visibility);
}

// What the occlusion takes out of a pixel's ambient: lit with its baked occlusion alone, less lit with that and the
// screen's combined by min. Blended as the HDR target less this, by its coverage.
@fragment
fn fs_ao_apply(@builtin(position) frag: vec4f) -> @location(0) vec4f {
    let pixel = vec2i(frag.xy);
    let surface = textureLoad(ao_normal, pixel, 0);
    if (surface.w <= 0.0) {
        return vec4f(0.0);
    }
    let ambient = textureLoad(ao_ambient, pixel, 0);
    let screen = textureLoad(ao_input, pixel, 0).r;
    let albedo = vec3f(surface.w);
    let baked = saturate(ambient.a);
    let lost = ao_multi_bounce(baked, albedo) - ao_multi_bounce(min(baked, screen), albedo);
    return vec4f(max(ambient.rgb * lost, vec3f(0.0)), 0.0);
}
