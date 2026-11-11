// Drawables' HDR colour to the display, as GTA V's post-process does it: the exposure from the frame's measured mean
// luminance (exposure.wgsl) through PS_CalculateExposure's curve and the timecycle's range, the bloom (bloom.wgsl)
// added as PS_Composite adds it, then PS_Composite's filmic curve (Hable's, visualsettings.dat's bright and dark settings, blended by the exposure) scaled so its white
// point is one. The game's colour correction, shift and desaturation are neutral in clear weather. Then its gamma:
// the game raises the colour to its Gamma itself (one over 2.2 at the brightness slider's middle) for a plain 8-bit
// target. Its dither is the window's, once the frame is done (present.wgsl). The HDR target holds premultiplied colour and coverage, so
// this is blended over what is already drawn (the backdrop) where there is a drawable; past its edges, its glow is
// added over the backdrop. The game also eases the exposure towards its target over time; here it is the target at
// once.
// Scene and the colour helpers come from scene.wgsl, the light from light.wgsl.

@group(1) @binding(0) var hdr_texture: texture_2d<f32>;
@group(1) @binding(1) var<storage, read> frame_mean: array<f32, 4>;
@group(1) @binding(2) var bloom_texture: texture_2d<f32>;
@group(1) @binding(3) var bloom_sampler: sampler;

@vertex
fn vs_tonemap(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
    let corner = vec2f(f32((index << 1u) & 2u), f32(index & 2u));
    return vec4f(corner * 2.0 - 1.0, 0.0, 1.0);
}

// PS_Composite's gamma: the tone-mapped colour raised to its Gamma, as the game writes it.
fn game_gamma(mapped: vec3f) -> vec3f {
    return pow(mapped, vec3f(light.filmic_bright1.w));
}

// The game's display colour for the frame, which goes to the window in sRGB: the window encodes what it's given with
// the sRGB curve, so it's given the colour that curve turns into the game's.
fn to_target(display: vec3f) -> vec3f {
    return srgb_to_linear(display);
}

// f(x) = (x(Ax + CB) + DE) / (x(Ax + B) + DF) - E/F, with p0 = (A, B, C, D) and p1 = (E, F, W).
fn filmic(x: vec3f, p0: vec4f, p1: vec4f) -> vec3f {
    return (x * (p0.x * x + p0.z * p0.y) + p0.w * p1.x) / (x * (p0.x * x + p0.y) + p0.w * p1.y) - p1.x / p1.y;
}

@fragment
fn fs_tonemap(@builtin(position) frag: vec4f) -> @location(0) vec4f {
    let stored = textureLoad(hdr_texture, vec2i(frag.xy), 0);
    let coverage = stored.a;
    let stops = exposure_stops(frame_mean[0]);
    let t = filmic_blend(stops);
    let p0 = mix(light.filmic_bright0, light.filmic_dark0, t);
    let p1 = mix(light.filmic_bright1, light.filmic_dark1, t);
    let white = filmic(vec3f(p1.z), p0, p1).x;
    let exposure = exp2(stops);
    // PS_Composite: the bloom, which the bright pass exposed, back to the scene's light, a quarter of it times the
    // timecycle's intensity.
    let uv = frag.xy / vec2f(textureDimensions(hdr_texture));
    let bloom = textureSampleLevel(bloom_texture, bloom_sampler, uv, 0.0).rgb / exposure * 0.25 * light.bloom.y;
    // Past the drawable, its glow alone, added over the backdrop; where there is no glow either, the backdrop as is.
    let glow = saturate(filmic(bloom * exposure, p0, p1) / white);
    if (coverage <= 0.0) {
        if (all(glow <= vec3f(0.0))) {
            discard;
        }
        return vec4f(to_target(game_gamma(glow)), 0.0);
    }
    let hdr = max(stored.rgb / coverage, vec3f(0.0));
    // PS_Composite keeps the scene and its bloom to half-float range, then exposes it.
    let exposed = min(hdr + bloom, vec3f(65504.0)) * exposure;
    let mapped = saturate(filmic(exposed, p0, p1) / white);
    // Premultiplied over the backdrop, with the glow over the part of the pixel the drawable doesn't cover.
    return vec4f(to_target(game_gamma(mapped)) * coverage + to_target(game_gamma(glow)) * (1.0 - coverage), coverage);
}
