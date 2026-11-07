// What GTA V's exposure is measured on (postfx PS_Lum*): the mean luminance (0.2125, 0.7154, 0.0721) of the whole
// HDR frame, before any exposure, with NaNs and infinities left out. The game's frame is its world and sky; the
// viewer's is the drawable over GTA's sky (sky_system.wgsl) and, below the horizon, the stand-in ground the
// reflections show. tonemap.wgsl turns the mean into the exposure with the game's curve.
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

// What a pixel of the frame holds: the drawable, premultiplied over the sky or ground behind it.
fn frame_pixel(pixel: vec2u, size: vec2u) -> f32 {
    let stored = textureLoad(hdr_frame, vec2i(pixel), 0);
    let ndc = vec2f((f32(pixel.x) + 0.5) / f32(size.x) * 2.0 - 1.0, 1.0 - (f32(pixel.y) + 0.5) / f32(size.y) * 2.0);
    let aspect = scene.projection.x;
    let tan_half = scene.projection.y;
    let ray = normalize(scene.forward.xyz + scene.right.xyz * ndc.x * aspect * tan_half + scene.up.xyz * ndc.y * tan_half);
    let behind = mix(sky_system(frame_sky, ray), frame_sky.ground.rgb, smoothstep(0.0, -0.04, ray.z));
    return frame_luminance(stored.rgb + (1.0 - stored.a) * behind);
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
