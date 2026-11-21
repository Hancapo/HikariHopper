// What the exposure is measured on, and the exposure eased towards what it measures, as GTA V's post-process does.
//   - The mean luminance (0.2125, 0.7154, 0.0721) of the HDR frame (postfx PS_Lum*), NaNs and infinities left out as
//     the game's conversion pass has it. A world's frame is measured as it is, as the game's always is.
//   - In the 3D viewer the frame is mostly one model, more or less of it as the camera comes and goes, so measured that
//     way the exposure would follow the framing, not the light. So the mean is of a fixed view of the world at that
//     hour instead: all round the horizon, at the elevations a level camera sees, GTA's sky (Gta5/sky_system.wgsl) above and
//     the stand-in ground the reflections show below, without the model. Wherever the camera is, the exposure is the
//     same; only the time of day moves it.
//   - The target exposure from the mean, PS_CalculateExposure's CalculateTargetExposure: the curve plus the tweak
//     (the timecycle's, the viewer's, and the push towards the sun), kept in the timecycle's range.
//   - The exposure itself, PS_CalculateExposure's: last frame's moved towards the target by the gap times
//     saturate(Adaptation.step.size.mult x dt) / dt stops a second, at most Adaptation.max.step.size, over the frame
//     time averaged over the last frames (dt); the target at once when that step is no more than
//     Adaptation.min.step.size, and on a reset (a camera cut, the first frame, the viewer's frames).
// Red Dead Redemption 2's world, lit in its own units, is exposed as its own post-process does instead
// (Rdr2Exposure.cs): its curve, scale ln(L + horizontal offset) + vertical offset - 10 + bending L, blended with its blend
// curve, then with the curve for its cube's luminance by the timecycle's cubemap amount, plus its tweak, in its range;
// and its adaptation, the step at least its least and at most its most, the target at once at its threshold. Its L is
// metered as the game's avgLuminanceCompute meters it: over the timecycle's spot meter, each pixel's luminance (at least
// visualsettings.dat's absolute least) between the least and the most there in a histogram of 256 bins, spaced evenly in
// ln(L); then the darkest lowlights percent and the brightest highlights percent left out, the mean of the middle
// (ComputeAverageLuminanceFromHistogramCS). The cube's luminance is the mean of its six faces' (LuminanceFromCubemap-
// LastMipCS): the game's is the reflection probe round the camera; the viewer's, which renders none, is the sky's
// ambient cube (Rdr2/sky.wgsl's).
// The work is laid out over the frame's size: per 16 by 16 tile, then their sum and the exposure. The tone map, the
// bloom and the lens flare take the exposure from here.
// Put after Scene/scene.wgsl and Gta5/sky_system.wgsl.

@group(1) @binding(0) var hdr_frame: texture_2d<f32>;
@group(1) @binding(1) var<storage, read_write> partial_sums: array<f32>;
@group(1) @binding(2) var<uniform> frame_sky: SkySystem;
@group(1) @binding(3) var<storage, read_write> measured: array<f32, 4>;

// RDR2's meter: its histogram's bins, and the least, the most and the mean luminance over the spot meter.
struct Rdr2Meter {
    bins: array<atomic<u32>, 256>,
    least: f32,
    most: f32,
    mean: f32,
    pixels: f32,
};
@group(1) @binding(5) var<storage, read_write> rdr2_meter: Rdr2Meter;
// RDR2's sky's light (Rdr2/sky.wgsl's sky_light): its ambient cube's faces after the key light.
@group(1) @binding(6) var<storage, read> rdr2_cube: array<vec4f, 7>;

// What the exposure is worked out with, written each frame.
struct ExposureSettings {
    // Whether the frame is measured as it is (a world's), whether the exposure takes its target at once, the frame
    // time averaged over the last frames, in seconds, and whether it's RDR2's exposure.
    mode: vec4f,
    // Exposure.curve.scale, Exposure.curve.power and Exposure.curve.offset, then the tweak; RDR2's: its curve's scale,
    // vertical offset, horizontal offset and bending.
    curve: vec4f,
    // The timecycle's least and most exposure.
    range: vec4f,
    // Adaptation.step.size.mult, Adaptation.max.step.size and Adaptation.min.step.size, then RDR2's threshold.
    adaptation: vec4f,
    // RDR2's blend curve, as its curve.
    blend_curve: vec4f,
    // RDR2's blend curve's weight, its tweak, and how much of the cube's luminance it takes.
    rdr2: vec4f,
    // RDR2's spot meter over the frame, from its top left to its bottom right (0 to 1).
    spot_meter: vec4f,
    // RDR2's lowlights and highlights fractions left out, and its least luminance.
    bands: vec4f,
};
@group(1) @binding(4) var<uniform> exposure_settings: ExposureSettings;

const LUMINANCE_GROUP: u32 = 16u;
var<workgroup> sums: array<f32, 256>;
var<workgroup> leasts: array<f32, 256>;
var<workgroup> mosts: array<f32, 256>;
var<workgroup> tile_bins: array<atomic<u32>, 256>;

fn frame_luminance(colour: vec3f) -> f32 {
    let l = dot(colour, vec3f(0.2125, 0.7154, 0.0721));
    // NaN and infinity count as nothing, as the game's conversion pass has it.
    return select(0.0, l, l == l && abs(l) < 3.0e38);
}

// RDR2's luminance of a pixel as its meter takes it: at least the least, which NaN and infinity count as.
fn rdr2_luminance(colour: vec3f) -> f32 {
    let least = exposure_settings.bands.z;
    let l = dot(colour, vec3f(0.2125, 0.7154, 0.0721));
    return select(max(l, least), least, !(l == l) || abs(l) >= 3.0e38);
}

// Whether a pixel is in RDR2's spot meter.
fn rdr2_metered(pixel: vec2u, size: vec2u) -> bool {
    let at = vec2f(pixel);
    let span = exposure_settings.spot_meter * vec4f(vec2f(size), vec2f(size));
    return all(at >= span.xy) && all(at < span.zw);
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
    var least = 3.4e38;
    var most = 0.0;
    if (id.x < size.x && id.y < size.y) {
        if (exposure_settings.mode.w < 0.5) {
            value = frame_pixel(id.xy, size);
            least = value;
            most = value;
        } else if (rdr2_metered(id.xy, size)) {
            // RDR2's: over its spot meter, the least and the most besides the sum.
            value = rdr2_luminance(textureLoad(hdr_frame, vec2i(id.xy), 0).rgb);
            least = value;
            most = value;
        }
    }
    sums[local] = value;
    leasts[local] = least;
    mosts[local] = most;
    workgroupBarrier();
    for (var stride = 128u; stride > 0u; stride = stride >> 1u) {
        if (local < stride) {
            sums[local] += sums[local + stride];
            leasts[local] = min(leasts[local], leasts[local + stride]);
            mosts[local] = max(mosts[local], mosts[local + stride]);
        }
        workgroupBarrier();
    }
    if (local == 0u) {
        let tile = group.y * groups.x + group.x;
        let count = groups.x * groups.y;
        partial_sums[tile] = sums[0];
        partial_sums[count + tile] = leasts[0];
        partial_sums[count * 2u + tile] = mosts[0];
    }
}

@compute @workgroup_size(256)
fn cs_luminance_mean(@builtin(local_invocation_index) local: u32) {
    let size = textureDimensions(hdr_frame, 0);
    let count = ((size.x + LUMINANCE_GROUP - 1u) / LUMINANCE_GROUP) * ((size.y + LUMINANCE_GROUP - 1u) / LUMINANCE_GROUP);
    var value = 0.0;
    var least = 3.4e38;
    var most = 0.0;
    for (var i = local; i < count; i += 256u) {
        value += partial_sums[i];
        least = min(least, partial_sums[count + i]);
        most = max(most, partial_sums[count * 2u + i]);
    }
    sums[local] = value;
    leasts[local] = least;
    mosts[local] = most;
    workgroupBarrier();
    for (var stride = 128u; stride > 0u; stride = stride >> 1u) {
        if (local < stride) {
            sums[local] += sums[local + stride];
            leasts[local] = min(leasts[local], leasts[local + stride]);
            mosts[local] = max(mosts[local], mosts[local + stride]);
        }
        workgroupBarrier();
    }
    // RDR2's: the spot meter's least, most and mean for its histogram, the histogram emptied; its exposure follows it.
    if (exposure_settings.mode.w > 0.5) {
        atomicStore(&rdr2_meter.bins[local], 0u);
        if (local == 0u) {
            let span = (exposure_settings.spot_meter.zw - exposure_settings.spot_meter.xy) * vec2f(size);
            rdr2_meter.pixels = max(span.x, 0.0) * max(span.y, 0.0);
            rdr2_meter.least = leasts[0];
            rdr2_meter.most = mosts[0];
            rdr2_meter.mean = sums[0] / max(rdr2_meter.pixels, 1.0);
        }
        return;
    }
    if (local == 0u) {
        let mean = sums[0] / f32(size.x * size.y);
        let target_stops = target_exposure(mean);
        measured[0] = mean;
        measured[1] = adapted_exposure(measured[1], target_stops);
        measured[2] = target_stops;
    }
}

// RDR2's histogram (ComputeSceneHistogramCS): each spot-metered pixel in its bin, 255 ln(L / least) / ln(most / least).
@compute @workgroup_size(16, 16)
fn cs_rdr2_histogram(@builtin(global_invocation_id) id: vec3u, @builtin(local_invocation_index) local: u32) {
    atomicStore(&tile_bins[local], 0u);
    workgroupBarrier();
    let size = textureDimensions(hdr_frame, 0);
    if (id.x < size.x && id.y < size.y && rdr2_metered(id.xy, size)) {
        let least = max(rdr2_meter.least, 1e-30);
        let span = log(rdr2_meter.most / least);
        let l = rdr2_luminance(textureLoad(hdr_frame, vec2i(id.xy), 0).rgb);
        var bin = 0u;
        if (span > 0.0) {
            bin = u32(clamp(log(l / least) / span * 255.0, 0.0, 255.0));
        }
        atomicAdd(&tile_bins[bin], 1u);
    }
    workgroupBarrier();
    let counted = atomicLoad(&tile_bins[local]);
    if (counted > 0u) {
        atomicAdd(&rdr2_meter.bins[local], counted);
    }
}

// RDR2's metered luminance and its exposure (ComputeAverageLuminanceFromHistogramCS, then PS_CalculateExposure): its
// bins taken darkest first, each at least (most / least)^(bin / 255), the lowlights' share of the pixels, then the
// middle's, the highlights' last; the middle's mean, and the cube's, are what the exposure's worked out from.
@compute @workgroup_size(1)
fn cs_rdr2_exposure() {
    // The pixels the histogram counted (the spot meter's within the frame); none metered keeps the exposure as it was.
    var pixels = 0.0;
    for (var bin = 0u; bin < 256u; bin++) {
        pixels += f32(atomicLoad(&rdr2_meter.bins[bin]));
    }
    if (pixels < 1.0) {
        return;
    }
    let least = max(rdr2_meter.least, 1e-30);
    let most = max(rdr2_meter.most, least);
    let lows = round(pixels * exposure_settings.bands.x);
    let highs = round(pixels * exposure_settings.bands.y);
    let middles = round(pixels - (lows + highs));
    let below_highs = lows + middles;
    var before = 0.0;
    var middle = 0.0;
    for (var bin = 0u; bin < 256u; bin++) {
        var count = f32(atomicLoad(&rdr2_meter.bins[bin]));
        let value = clamp(least * pow(max(most / least, 0.0), f32(bin) / 255.0), least, most);
        // The lowlights' first, then the middle's from what's left.
        let low = clamp(lows - before, 0.0, count);
        count -= low;
        let mid = clamp(below_highs - (before + low), 0.0, count);
        middle += mid * value;
        before += f32(atomicLoad(&rdr2_meter.bins[bin]));
    }
    let metered = select(0.0, middle / middles, middles > 0.0);
    // The cube's luminance: the mean of its faces', each at least the least.
    var cube = 0.0;
    for (var face = 1; face < 7; face++) {
        cube += rdr2_luminance(rdr2_cube[face].rgb) / 6.0;
    }
    let target_stops = rdr2_target_exposure(metered, cube);
    measured[0] = metered;
    measured[1] = adapted_exposure(measured[1], target_stops);
    measured[2] = target_stops;
    measured[3] = cube;
}

// RDR2's target (PS_CalculateExposure): its curve blended with its blend curve by the weight, then with its curve for
// the cube's luminance by the cubemap amount, plus its tweak, in its range.
fn rdr2_target_exposure(metered: f32, cube: f32) -> f32 {
    let curve = exposure_settings.curve;
    let main = rdr2_curve(curve, metered);
    var blended = main + (rdr2_curve(exposure_settings.blend_curve, metered) - main) * exposure_settings.rdr2.x;
    blended += (rdr2_curve(curve, cube) - blended) * exposure_settings.rdr2.z;
    return clamp(blended + exposure_settings.rdr2.y, exposure_settings.range.x, exposure_settings.range.y);
}

// CalculateTargetExposure: the curve from the mean luminance, plus the tweak, in the timecycle's range (the game's push
// away from nought is a cutscene camera's).
fn target_exposure(mean: f32) -> f32 {
    let curve = exposure_settings.curve;
    let range = exposure_settings.range;
    return clamp(curve.x * pow(max(mean, 1e-6), curve.y) + curve.z + curve.w, range.x, range.y);
}

// RDR2's curve for a luminance: scale ln(L + horizontal offset) + vertical offset - 10 + bending L.
fn rdr2_curve(curve: vec4f, luminance: f32) -> f32 {
    return curve.x * log(max(luminance + curve.z, 1e-30)) + curve.y - 10.0 + curve.w * luminance;
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
    if (exposure_settings.mode.w > 0.5) {
        // RDR2's: at least the least step and at most the most, the target at once at the threshold.
        let step = clamp(abs(gap) * multiplier, adaptation.z, adaptation.y);
        if (step <= adaptation.w || gap == 0.0) {
            return clamp(target_stops, range.x, range.y);
        }
        let moved = previous + sign(gap) * step * dt;
        return clamp(select(min(moved, target_stops), max(moved, target_stops), gap < 0.0), range.x, range.y);
    }
    let rate = min(adaptation.y, abs(gap) * multiplier);
    var exposure = target_stops;
    if (rate > adaptation.z && gap != 0.0) {
        exposure = previous + rate * sign(gap) * dt;
        exposure = select(clamp(exposure, previous, target_stops), clamp(exposure, target_stops, previous), gap < 0.0);
    }
    return clamp(exposure, range.x, range.y);
}
