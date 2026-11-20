// What the exposure is measured on, and the exposure eased towards what it measures, as GTA V's post-process does.
//   - The mean luminance (0.2125, 0.7154, 0.0721) of the HDR frame (postfx PS_Lum*), NaNs and infinities left out as
//     the game's conversion pass has it. A world's frame is measured as it is, as the game's always is.
//   - In the 3D viewer the frame is mostly one model, more or less of it as the camera comes and goes, so measured that
//     way the exposure would follow the framing, not the light. So the mean is of a fixed view of the world at that
//     hour instead: all round the horizon, at the elevations a level camera sees, GTA's sky (sky_system.wgsl) above and
//     the stand-in ground the reflections show below, without the model. Wherever the camera is, the exposure is the
//     same; only the time of day moves it.
//   - The target exposure from the mean, PS_CalculateExposure's CalculateTargetExposure: the curve plus the tweak
//     (the timecycle's, the viewer's, and the push towards the sun), kept in the timecycle's range.
//   - The exposure itself, PS_CalculateExposure's: last frame's moved towards the target by the gap times
//     saturate(Adaptation.step.size.mult x dt) / dt stops a second, at most Adaptation.max.step.size, over the frame
//     time averaged over the last frames (dt); the target at once when that step is no more than
//     Adaptation.min.step.size, and on a reset (a camera cut, the first frame, the viewer's frames).
// The work is laid out over the frame's size: per 16 by 16 tile, then their sum and the exposure. The tone map, the
// bloom and the lens flare take the exposure from here.
// Put after scene.wgsl and sky_system.wgsl.

@group(1) @binding(0) var hdr_frame: texture_2d<f32>;
@group(1) @binding(1) var<storage, read_write> partial_sums: array<f32>;
@group(1) @binding(2) var<uniform> frame_sky: SkySystem;
@group(1) @binding(3) var<storage, read_write> measured: array<f32, 4>;

// What the exposure is worked out with, written each frame.
struct ExposureSettings {
    // Whether the frame is measured as it is (a world's), whether the exposure takes its target at once, and the
    // frame time averaged over the last frames, in seconds.
    mode: vec4f,
    // Exposure.curve.scale, Exposure.curve.power and Exposure.curve.offset, then the tweak.
    curve: vec4f,
    // The timecycle's least and most exposure.
    range: vec4f,
    // Adaptation.step.size.mult, Adaptation.max.step.size and Adaptation.min.step.size.
    adaptation: vec4f,
};
@group(1) @binding(4) var<uniform> exposure_settings: ExposureSettings;

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

// A world's frame as it is; in the viewer, what the world shows in that pixel's direction at this hour: the sky, or
// the ground below the horizon.
fn frame_pixel(pixel: vec2u, size: vec2u) -> f32 {
    if (exposure_settings.mode.x > 0.5) {
        return frame_luminance(textureLoad(hdr_frame, vec2i(pixel), 0).rgb);
    }
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
        let mean = sums[0] / f32(size.x * size.y);
        let target_stops = target_exposure(mean);
        measured[0] = mean;
        measured[1] = adapted_exposure(measured[1], target_stops);
        measured[2] = target_stops;
    }
}

// CalculateTargetExposure: the curve from the mean luminance, plus the tweak, in the timecycle's range (the game's push
// away from nought is a cutscene camera's).
fn target_exposure(mean: f32) -> f32 {
    let curve = exposure_settings.curve;
    let range = exposure_settings.range;
    return clamp(curve.x * pow(max(mean, 1e-6), curve.y) + curve.z + curve.w, range.x, range.y);
}

// PS_CalculateExposure: last frame's exposure moved towards the target, as fast as the gap times the step multiplier
// (over the averaged frame time), at most the largest step; the target at once for a step no more than the smallest,
// or on a reset; kept in the timecycle's range.
fn adapted_exposure(previous: f32, target_stops: f32) -> f32 {
    let range = exposure_settings.range;
    let dt = exposure_settings.mode.z;
    if (exposure_settings.mode.y > 0.5 || dt <= 0.0 || !(previous == previous) || abs(previous) > 1e6) {
        return target_stops;
    }
    let adaptation = exposure_settings.adaptation;
    let multiplier = saturate(adaptation.x * dt) / dt;
    let gap = target_stops - previous;
    let rate = min(adaptation.y, abs(gap) * multiplier);
    var exposure = target_stops;
    if (rate > adaptation.z && gap != 0.0) {
        exposure = previous + rate * sign(gap) * dt;
        exposure = select(clamp(exposure, previous, target_stops), clamp(exposure, target_stops, previous), gap < 0.0);
    }
    return clamp(exposure, range.x, range.y);
}
