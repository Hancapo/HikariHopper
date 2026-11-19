// Drawables' HDR colour to the display, as GTA V's post-process does it: the exposure exposure.wgsl works out
// (PS_CalculateExposure's, eased over the frames towards what the frame's mean luminance asks for), the bloom (bloom.wgsl)
// added as PS_Composite adds it, then PS_Composite's filmic curve (Hable's, visualsettings.dat's bright and dark settings, blended by the exposure) scaled so its white
// point is one. The game's colour correction, shift and desaturation are neutral in clear weather. Then its gamma:
// the game raises the colour to its Gamma itself (one over 2.2 at the brightness slider's middle) for a plain 8-bit
// target. Its dither is the window's, once the frame is done (present.wgsl). The HDR target holds premultiplied colour and coverage, so
// this is blended over what is already drawn (the backdrop) where there is a drawable; past its edges, its glow is
// added over the backdrop.
// Red Dead Redemption 2's world is graded after its tone map as its PS_Composite grades it (Rdr2Grading.cs): the colour
// to the display's sRGB (the game's own transform, not GTA V's gamma); the scotopic shift, the colour's scotopic
// luminance (from its XYZ: Y (1.33 (1 + (Y + Z) / X) - 1.68)) times the timecycle's scotopic colour and intensity,
// mixed in by saturate(0.04 / (L + 0.04) scale + bias) for the scene's luminance L before its exposure; then its levels
// table and its weather's, each a 16 by 16 by 16 table read as the game reads it.
// Scene and the colour helpers come from scene.wgsl, the light from light.wgsl.

@group(1) @binding(0) var hdr_texture: texture_2d<f32>;
@group(1) @binding(1) var<storage, read> frame_mean: array<f32, 4>;
@group(1) @binding(2) var bloom_texture: texture_2d<f32>;
@group(1) @binding(3) var bloom_sampler: sampler;

// RDR2's grading: whether it's on; the scotopic colour and its intensity; the scotopic weight's scale and bias.
struct Rdr2Grade {
    on: vec4f,
    scotopic: vec4f,
    scotopic_weight: vec4f,
};
@group(1) @binding(4) var<uniform> rdr2_grade: Rdr2Grade;
@group(1) @binding(5) var rdr2_levels: texture_2d<f32>;
@group(1) @binding(6) var rdr2_weather: texture_2d<f32>;

// A colour through one of RDR2's tables: blue picks the two slices either side, red across and green down each.
fn rdr2_table(table: texture_2d<f32>, colour: vec3f) -> vec3f {
    let c = saturate(colour);
    let slice = floor(c.b * 14.9999);
    let along = c.b * 15.0 - slice;
    let at = vec2f(slice * 0.0625 + c.r * 0.058594 + 0.001953, c.g * 0.9375 + 0.03125);
    let below = textureSampleLevel(table, bloom_sampler, at, 0.0).rgb;
    let above = textureSampleLevel(table, bloom_sampler, at + vec2f(0.0625, 0.0), 0.0).rgb;
    return mix(below, above, along);
}

// RDR2's display colour for a tone-mapped one, at the scene's luminance before its exposure, as the window takes it.
fn rdr2_display(mapped: vec3f, luminance: f32) -> vec3f {
    var display = linear_to_srgb(saturate(mapped));
    let x = max(dot(display, vec3f(0.5149, 0.3244, 0.1607)), 0.01);
    let y = max(dot(display, vec3f(0.2654, 0.6704, 0.0642)), 0.0);
    let z = max(dot(display, vec3f(0.0248, 0.1248, 0.8504)), 0.0);
    let scotopic = y * (1.33 * (1.0 + (y + z) / x) - 1.68);
    let night = saturate(scotopic * rdr2_grade.scotopic.rgb * rdr2_grade.scotopic.w);
    let weight = saturate(0.04 / (luminance + 0.04) * rdr2_grade.scotopic_weight.x + rdr2_grade.scotopic_weight.y);
    display = mix(display, night, weight);
    display = rdr2_table(rdr2_levels, display);
    display = rdr2_table(rdr2_weather, display);
    return srgb_to_linear(display);
}

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
    // The exposure exposure.wgsl eased towards the frame's.
    let stops = frame_mean[1];
    let t = filmic_blend(stops);
    let p0 = mix(light.filmic_bright0, light.filmic_dark0, t);
    let p1 = mix(light.filmic_bright1, light.filmic_dark1, t);
    let white = filmic(vec3f(p1.z), p0, p1).x;
    let exposure = exp2(stops);
    // PS_Composite: the bloom, which the bright pass exposed, back to the scene's light, a quarter of it times the
    // timecycle's intensity.
    let window_size = scene.viewport.xy;
    let uv = frag.xy / window_size;
    let bloom = textureSampleLevel(bloom_texture, bloom_sampler, uv, 0.0).rgb / exposure * 0.25 * light.bloom.y;
    // Past the drawable, its glow alone, added over the backdrop; where there is no glow either, the backdrop as is.
    let glow = saturate(filmic(bloom * exposure, p0, p1) / white);
    // The HDR target at the window's size is read pixel for pixel. At another render scale it is read across the
    // pixel and each read tone-mapped before they are averaged, so highlights don't blow up as they shrink: two by two
    // reads when it is larger (supersampled; at twice the size, each of its pixels once), one bilinear read when smaller.
    // A read between its pixels tone-maps the four around it and blends those, rather than blend the HDR colour first.
    let size = vec2f(textureDimensions(hdr_texture));
    var result = vec4f(0.0);
    if (all(abs(size - window_size) < vec2f(0.5))) {
        result = composite(textureLoad(hdr_texture, vec2i(frag.xy), 0), bloom, glow, exposure, p0, p1, white);
    } else {
        let n = select(1, 2, size.x > window_size.x + 0.5);
        for (var y = 0; y < n; y++) {
            for (var x = 0; x < n; x++) {
                let at = (floor(frag.xy) + (vec2f(f32(x), f32(y)) + 0.5) / f32(n)) / window_size;
                result += composite_between(at * size, bloom, glow, exposure, p0, p1, white);
            }
        }
        result /= f32(n * n);
    }
    if (all(result == vec4f(0.0))) {
        discard;
    }
    return result;
}

// The HDR target at a point between its pixels (in its pixels, from its corner): the four pixels around it, each
// tone-mapped, blended by how near the point is to each, as a bilinear read would blend them.
fn composite_between(place: vec2f, bloom: vec3f, glow: vec3f, exposure: f32, p0: vec4f, p1: vec4f, white: f32) -> vec4f {
    let last = vec2i(textureDimensions(hdr_texture)) - 1;
    let centred = place - 0.5;
    let corner = vec2i(floor(centred));
    let weight = centred - floor(centred);
    var sum = vec4f(0.0);
    for (var y = 0; y < 2; y++) {
        for (var x = 0; x < 2; x++) {
            let texel = clamp(corner + vec2i(x, y), vec2i(0), last);
            let w = select(1.0 - weight.x, weight.x, x == 1) * select(1.0 - weight.y, weight.y, y == 1);
            if (w > 0.0) {
                sum += w * composite(textureLoad(hdr_texture, texel, 0), bloom, glow, exposure, p0, p1, white);
            }
        }
    }
    return sum;
}

// One read of the HDR target, tone-mapped and premultiplied by its coverage over the backdrop, with the glow over the
// part of it the drawable doesn't cover.
fn composite(stored: vec4f, bloom: vec3f, glow: vec3f, exposure: f32, p0: vec4f, p1: vec4f, white: f32) -> vec4f {
    let coverage = stored.a;
    if (coverage <= 0.0) {
        return vec4f(to_target(game_gamma(glow)), 0.0);
    }
    let hdr = max(stored.rgb / coverage, vec3f(0.0));
    // PS_Composite keeps the scene and its bloom to half-float range, then exposes it.
    let exposed = min(hdr + bloom, vec3f(65504.0)) * exposure;
    let mapped = saturate(filmic(exposed, p0, p1) / white);
    if (rdr2_grade.on.x > 0.5) {
        let display = rdr2_display(mapped, dot(hdr, vec3f(0.3, 0.59, 0.11)));
        return vec4f(display * coverage + to_target(game_gamma(glow)) * (1.0 - coverage), coverage);
    }
    return vec4f(to_target(game_gamma(mapped)) * coverage + to_target(game_gamma(glow)) * (1.0 - coverage), coverage);
}
