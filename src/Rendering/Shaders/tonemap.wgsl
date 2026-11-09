// Drawables' HDR colour to the display, as GTA V's post-process does it: the exposure from the frame's measured mean
// luminance (exposure.wgsl) through PS_CalculateExposure's curve and the timecycle's range, then PS_Composite's
// filmic curve (Hable's, visualsettings.dat's bright and dark settings, blended by the exposure) scaled so its white
// point is one. The game's colour correction, shift and desaturation are neutral in clear weather, and its gamma is
// the sRGB target's. A dither in display space follows. The HDR target holds premultiplied colour and coverage, so
// this is blended over what is already drawn (the backdrop) where there is a drawable. The game also eases the
// exposure towards its target over time; here it is the target at once.
// Scene and the colour helpers come from scene.wgsl, the light from light.wgsl.

@group(1) @binding(0) var hdr_texture: texture_2d<f32>;
@group(1) @binding(1) var<storage, read> frame_mean: array<f32, 4>;

@vertex
fn vs_tonemap(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
    let corner = vec2f(f32((index << 1u) & 2u), f32(index & 2u));
    return vec4f(corner * 2.0 - 1.0, 0.0, 1.0);
}

// f(x) = (x(Ax + CB) + DE) / (x(Ax + B) + DF) - E/F, with p0 = (A, B, C, D) and p1 = (E, F, W).
fn filmic(x: vec3f, p0: vec4f, p1: vec4f) -> vec3f {
    return (x * (p0.x * x + p0.z * p0.y) + p0.w * p1.x) / (x * (p0.x * x + p0.y) + p0.w * p1.y) - p1.x / p1.y;
}

// The exposure in stops for the measured luminance: scale * L^power + offset + the timecycle's exposure, in range.
fn exposure_stops() -> f32 {
    let curve = light.exposure_curve;
    let range = light.exposure_range;
    return clamp(curve.x * pow(max(frame_mean[0], 1e-6), curve.y) + curve.z + curve.w, range.x, range.y);
}

@fragment
fn fs_tonemap(@builtin(position) frag: vec4f) -> @location(0) vec4f {
    let stored = textureLoad(hdr_texture, vec2i(frag.xy), 0);
    let coverage = stored.a;
    if (coverage <= 0.0) {
        discard;
    }
    let stops = exposure_stops();
    let range = light.exposure_range;
    let t = saturate((stops - range.z) / (range.w - range.z));
    let p0 = mix(light.filmic_bright0, light.filmic_dark0, t);
    let p1 = mix(light.filmic_bright1, light.filmic_dark1, t);
    let white = filmic(vec3f(p1.z), p0, p1).x;
    let hdr = max(stored.rgb / coverage, vec3f(0.0));
    let exposed = min(hdr, vec3f(30000.0)) * exp2(stops);
    let mapped = saturate(filmic(exposed, p0, p1) / white);
    // Premultiplied over the backdrop.
    return vec4f(dither(mapped, frag.xy) * coverage, coverage);
}
