// GTA V's sub-sampled alpha combine (PS_SubSampleAlphaSinglePass, the single pass the game runs on PC, checked against
// Enhanced's): after the frame's surfaces, coronas and lens flare, before the post-process. A cut-out's sub-sampled
// pixels (surface.wgsl's sub_sample) hold it where its alpha isn't whole, and leave the checkerboard's other pixels to
// what is behind; each is blended with its neighbour to the right by the alpha it left, so both show the cut-out over
// what is behind it by that alpha. What no cut-out left is kept as it is. Standalone (no scene.wgsl).
//
// The HDR target holds premultiplied colour and coverage: where both pixels are covered (all of a world's frame) the
// colour is the game's own blend; where one is the backdrop, the covered one's colour is kept, its coverage blended.

// A copy of the HDR target as the frame's surfaces left it, and the sub-sampled alpha they left beside it.
@group(0) @binding(0) var frame: texture_2d<f32>;
@group(0) @binding(1) var sub_sampled: texture_2d<f32>;

@vertex
fn vs_subsample_alpha(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
    let corner = vec2f(f32((index << 1u) & 2u), f32(index & 2u));
    return vec4f(corner * 2.0 - 1.0, 0.0, 1.0);
}

// The alpha a cut-out left in sevenths, at least a thousandth; nought where none did.
fn stored_alpha(pixel: vec2i) -> f32 {
    let bits = u32(textureLoad(sub_sampled, pixel, 0).r * 255.0 + 0.5);
    if ((bits & 8u) == 0u) {
        return 0.0;
    }
    return max(0.001, f32(bits & 7u) * (1.0 / 7.0));
}

// The game's blend of two HDR colours, from under to over: their luminance in log, their colour over it linearly.
fn combined(under: vec3f, over: vec3f, amount: f32) -> vec3f {
    let weights = vec3f(0.3, 0.59, 0.11);
    let under_level = max(dot(under, weights), 0.0001);
    let over_level = max(dot(over, weights), 0.0001);
    let colour = mix(under / under_level, over / over_level, amount);
    let level = exp2(mix(log2(under_level * 0.4), log2(over_level * 0.4), amount)) * 2.5;
    return level * colour;
}

fn unpremultiplied(stored: vec4f) -> vec3f {
    return select(vec3f(0.0), stored.rgb / max(stored.a, 1e-6), stored.a > 0.0);
}

@fragment
fn fs_subsample_alpha(@builtin(position) position: vec4f) -> @location(0) vec4f {
    let pixel = vec2i(position.xy);
    let next = vec2i(min(pixel.x + 1, i32(textureDimensions(sub_sampled).x) - 1), pixel.y);
    let own = stored_alpha(pixel);
    // surface.wgsl's ssa_opaque_pixel: where the cut-out kept only what is whole.
    let opaque = fract(dot(position.xy, vec2f(0.5))) < 0.5;
    var amount: f32;
    if (opaque) {
        // What is behind, towards the cut-out beside it by that one's alpha; left alone where both are whole.
        let beside = stored_alpha(next);
        if (beside == 0.0 || (beside == 1.0 && own == 1.0)) {
            discard;
        }
        amount = beside;
    } else {
        // The cut-out, from what is behind it beside it, by its alpha.
        if (own == 0.0 || own == 1.0) {
            discard;
        }
        amount = own;
    }
    let here = textureLoad(frame, pixel, 0);
    let there = textureLoad(frame, next, 0);
    let under = select(there, here, opaque);
    let over = select(here, there, opaque);
    var colour = unpremultiplied(select(under, over, under.a <= 0.0));
    if (under.a > 0.0 && over.a > 0.0) {
        colour = combined(unpremultiplied(under), unpremultiplied(over), amount);
    }
    let coverage = mix(under.a, over.a, amount);
    return vec4f(colour * coverage, coverage);
}
