// Pure material operations shared by the renderer and GPU regression checks.
fn terrain_mask(rgb: vec3f) -> vec4f {
    return vec4f(rgb, clamp(1.0 - dot(rgb, vec3f(1.0)), 0.0, 1.0));
}

fn terrain_lod_weights(painted: vec4f) -> vec4f {
    return vec4f(1.0 - painted.g, painted.g, 0.0, 0.0);
}

fn terrain_hbb_weights(lookup: vec3f, painted: vec4f, heights: vec4f,
    range_scale: vec4f, range_bias: vec4f, softness: vec4f) -> vec4f {
    return terrain_height_weights(terrain_painted_weights(lookup, painted, false), heights, range_scale, range_bias, softness);
}

fn terrain_painted_weights(lookup: vec3f, painted: vec4f, two_layers: bool) -> vec4f {
    var lookup_mask = terrain_mask(lookup);
    // The two-layer program explicitly zeros lookup.w; painted weights retain their authored lanes.
    if (two_layers) { lookup_mask.w = 0.0; }
    return mix(lookup_mask, terrain_mask(painted.rgb), painted.a);
}

fn terrain_height_weights(mask: vec4f, heights: vec4f,
    range_scale: vec4f, range_bias: vec4f, softness: vec4f) -> vec4f {
    let height = max((heights - range_bias) * range_scale, vec4f(0.01));
    let weighted = clamp(mask * height, vec4f(0.0), vec4f(1.0));
    let peak = max(max(weighted.x, weighted.y), max(weighted.z, weighted.w));
    if (peak <= 0.0) { return vec4f(1.0, 0.0, 0.0, 0.0); }
    let exponent = vec4f(20.0) - 19.0 * clamp((softness - vec4f(0.2)) * 1.754386, vec4f(0.0), vec4f(1.0));
    let sharpened = pow(weighted / peak, exponent) - vec4f(0.001);
    // The subtraction precedes the sum and saturation in the native program.
    return clamp(sharpened / dot(sharpened, vec4f(1.0)), vec4f(0.0), vec4f(1.0));
}

fn terrain_layer_tint(colour: vec3f, macro_colour: vec3f, mask: f32, intensity: f32, blend: f32) -> vec3f {
    let luminance = dot(colour, vec3f(0.30, 0.59, 0.11));
    return mix(colour, luminance * intensity * max(macro_colour, vec3f(0.0001)), clamp(mask * blend, 0.0, 1.0));
}

fn terrain_normal_rg(encoded: vec2f, power: f32) -> vec3f {
    let xy = encoded * 2.0 - vec2f(1.0);
    let z = sqrt(max(1.0 - dot(xy, xy), 0.0001));
    return normalize(vec3f(xy * power, z));
}

fn terrain_reorient_normal(detail: vec3f, encoded_macro: vec2f) -> vec3f {
    let xy = encoded_macro * 2.0 - vec2f(1.0);
    let t = vec3f(xy, sqrt(max(1.0 - dot(xy, xy), 0.0001)) + 1.0);
    let u = detail * vec3f(-1.0, -1.0, 1.0);
    return t * (dot(t, u) / t.z) - u;
}

fn decal_hbb_coverage(height: f32, alpha: f32, softness: f32, fake_cutout: bool) -> f32 {
    if (fake_cutout) { return select(0.0, 1.0, height > 0.0 && height + alpha >= 1.0); }
    let numerator = height * alpha;
    let denominator = max(numerator, (1.0 - height) * (1.0 - alpha));
    // Define the native 0/0 corner as uncovered rather than propagate NaN.
    if (denominator <= 0.0) { return 0.0; }
    return pow(numerator / denominator, 15.0 - 14.0 * softness);
}
