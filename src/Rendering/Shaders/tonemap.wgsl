// Drawables' HDR colour to the display, after RedFury's tone-map pass (viewer_post.wgsl): exposure, day-cycle eye
// adaptation, the Khronos PBR Neutral shoulder (identity below 0.76, so the theme's colours stay as they are), and
// a dither in display space. The HDR target holds premultiplied colour and coverage, so this is blended over what is
// already drawn (the backdrop) where there is a drawable.
// Scene and the colour helpers come from scene.wgsl, the light from light.wgsl.

@group(1) @binding(0) var hdr_texture: texture_2d<f32>;

const TONEMAP_PI: f32 = 3.14159265359;

@vertex
fn vs_tonemap(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
    let corner = vec2f(f32((index << 1u) & 2u), f32(index & 2u));
    return vec4f(corner * 2.0 - 1.0, 0.0, 1.0);
}

// Khronos PBR Neutral shoulder: identity up to 0.76, then a hue-preserving roll-off towards white. The reference toe
// (a 0.04 black offset for display flare) is omitted, as in RedFury: it crushes shadowed albedo.
fn neutral_shoulder(colour: vec3f) -> vec3f {
    let start_compression = 0.8 - 0.04;
    let desaturation = 0.15;
    let peak = max(colour.r, max(colour.g, colour.b));
    if (peak < start_compression) {
        return colour;
    }
    let d = 1.0 - start_compression;
    let new_peak = 1.0 - d * d / (peak + d - start_compression);
    let g = 1.0 - 1.0 / (desaturation * (peak - new_peak) + 1.0);
    return mix(colour * (new_peak / peak), vec3f(new_peak), g);
}

// RedFury's eye adaptation: brightens dim scenes towards a daylight reference, measured as the up-facing sky
// irradiance plus the direct key light, both over pi. Partial (an exponent of 0.55): a moonlit night stays about
// four stops darker than day.
const ADAPTATION_REFERENCE: f32 = 1.0;
const ADAPTATION_STRENGTH: f32 = 0.55;
const ADAPTATION_MAX: f32 = 256.0;

fn adaptation() -> f32 {
    let h = sky_irradiance;
    let sky_up = h[0].rgb * 0.282095 + h[2].rgb * 0.488603 + h[6].rgb * 0.630784;
    let direct = light.sun_illuminance.rgb * max(light.sun_direction.z, 0.0) / TONEMAP_PI;
    let brightness = dot(max(sky_up, vec3f(0.0)) + direct, vec3f(0.2126, 0.7152, 0.0722));
    return clamp(pow(ADAPTATION_REFERENCE / max(brightness, 1e-7), ADAPTATION_STRENGTH), 1.0, ADAPTATION_MAX);
}

@fragment
fn fs_tonemap(@builtin(position) frag: vec4f) -> @location(0) vec4f {
    let stored = textureLoad(hdr_texture, vec2i(frag.xy), 0);
    let coverage = stored.a;
    if (coverage <= 0.0) {
        discard;
    }
    let hdr = max(stored.rgb / coverage, vec3f(0.0));
    let mapped = clamp(neutral_shoulder(min(hdr, vec3f(30000.0)) * light.sun_direction.w * adaptation()), vec3f(0.0), vec3f(1.0));
    // Premultiplied over the backdrop.
    return vec4f(dither(mapped, frag.xy) * coverage, coverage);
}
