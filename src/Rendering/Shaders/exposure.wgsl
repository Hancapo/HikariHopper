// What the viewer's exposure is measured on. GTA V measures the mean luminance (0.2125, 0.7154, 0.0721) of its whole
// HDR frame (postfx PS_Lum*), which in the game is always its world and sky. In a viewer the frame is mostly one model,
// more or less of it as the camera comes and goes, so measured that way the exposure would follow the framing, not the
// light. So the mean is of a fixed view of the world at that hour instead: all round the horizon, at the elevations a
// level camera sees, GTA's sky (sky_system.wgsl) above and the stand-in ground the reflections show below, without the
// model. Wherever the camera is, the exposure is the same; only the time of day moves it. The work is laid out over
// the frame's size, each of its pixels one direction of that view. tonemap.wgsl turns the mean into the exposure with
// the game's curve. NaNs and infinities are left out, as the game's conversion pass has it.
// Put after scene.wgsl and sky_system.wgsl.

@group(1) @binding(0) var hdr_frame: texture_2d<f32>;
@group(1) @binding(1) var<storage, read_write> partial_sums: array<f32>;
@group(1) @binding(2) var<uniform> frame_sky: SkySystem;
@group(1) @binding(3) var<storage, read_write> measured: array<f32, 4>;

const LUMINANCE_GROUP: u32 = 16u;
var<workgroup> sums: array<f32, 256>;

fn frame_luminance(colour: vec3f) -> f32 {
    let l = dot(colour, vec3f(0.2125, 0.7154, 0.0721));
    // NaN and infinity count as nothing, as the game's conversion pass has it.
    return select(0.0, l, l == l && abs(l) < 3.0e38);
}

// The direction a pixel of the work stands for: across, all round the horizon; down, from the top to the bottom of
// a level camera's view.
fn reference_ray(pixel: vec2u, size: vec2u) -> vec3f {
    let around = (f32(pixel.x) + 0.5) / f32(size.x) * 6.2831853;
    let tan_half = scene.projection.y;
    let rise = (1.0 - (f32(pixel.y) + 0.5) / f32(size.y) * 2.0) * tan_half;
    return normalize(vec3f(cos(around), sin(around), rise));
}

// What the world shows in that direction at this hour: the sky, or the ground below the horizon.
fn frame_pixel(pixel: vec2u, size: vec2u) -> f32 {
    let ray = reference_ray(pixel, size);
    return frame_luminance(mix(sky_system(frame_sky, ray), frame_sky.ground.rgb, smoothstep(0.0, -0.04, ray.z)));
}

@compute @workgroup_size(16, 16)
fn cs_luminance(@builtin(global_invocation_id) id: vec3u, @builtin(local_invocation_index) local: u32,
    @builtin(workgroup_id) group: vec3u, @builtin(num_workgroups) groups: vec3u) {
    let size = textureDimensions(hdr_frame, 0);
    var value = 0.0;
    if (id.x < size.x && id.y < size.y) {
        value = frame_pixel(id.xy, size);
    }
    sums[local] = value;
    workgroupBarrier();
    for (var stride = 128u; stride > 0u; stride = stride >> 1u) {
        if (local < stride) {
            sums[local] += sums[local + stride];
        }
        workgroupBarrier();
    }
    if (local == 0u) {
        partial_sums[group.y * groups.x + group.x] = sums[0];
    }
}

@compute @workgroup_size(256)
fn cs_luminance_mean(@builtin(local_invocation_index) local: u32) {
    let size = textureDimensions(hdr_frame, 0);
    let count = ((size.x + LUMINANCE_GROUP - 1u) / LUMINANCE_GROUP) * ((size.y + LUMINANCE_GROUP - 1u) / LUMINANCE_GROUP);
    var value = 0.0;
    for (var i = local; i < count; i += 256u) {
        value += partial_sums[i];
    }
    sums[local] = value;
    workgroupBarrier();
    for (var stride = 128u; stride > 0u; stride = stride >> 1u) {
        if (local < stride) {
            sums[local] += sums[local + stride];
        }
        workgroupBarrier();
    }
    if (local == 0u) {
        measured[0] = sums[0] / f32(size.x * size.y);
    }
}
