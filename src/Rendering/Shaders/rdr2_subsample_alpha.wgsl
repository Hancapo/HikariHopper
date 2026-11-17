// Red Dead Redemption 2's sub-sampled alpha, put together over a copy of the HDR target once the solid surfaces are drawn
// (rdr2_cutout.wgsl says how they mark it): the game's postfx PS_SubSampleAlphaSinglePass, its blend as the game's.
// A pixel a cut-out kept on the checkerboard's keeping half with partial coverage holds that coverage in its alpha, over
// 2. It's blended with what's behind it, by its coverage; a pixel of the dropping half next to it, which shows what's
// behind the cut-out, with it by the same. The game pairs each pixel with the one to its right, whose colour it takes
// whole once its TAA has averaged the checkerboard; without a TAA, a cut-out a pixel wide (a fence's gaps, a twig) would
// keep the checkerboard where the pixel to the right isn't the cut-out's, so a pixel takes the one to its right as the
// game does when that one is the other half of the cut-out, and else the others around it that are: a dropped pixel the
// kept ones nearer than it, a kept pixel the dropped ones farther than it, by the depth.
// As the game blends them: their colours over their luminance mixed straight, their luminance, from a 0.0001 floor, as
// its logarithm. Over the backdrop of the model viewer (a pixel the HDR target covers only in part), mixed straight
// instead, colour and coverage. Every other pixel is left as it is.

@group(0) @binding(0) var scene_copy: texture_2d<f32>;
@group(0) @binding(1) var scene_depth: texture_depth_2d;

@vertex
fn vs_sub_sample_alpha(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
    let corner = vec2f(f32((index << 1u) & 2u), f32(index & 2u));
    return vec4f(corner * 2.0 - 1.0, 0.0, 1.0);
}

// The coverage a pixel's alpha marks, nought for none: the game's at least 0.001 where marked.
fn marked_coverage(stored: vec4f) -> f32 {
    return select(0.0, max(stored.a - 2.0, 0.001), stored.a > 1.5);
}

fn luminance(colour: vec3f) -> f32 {
    return max(dot(colour, vec3f(0.3, 0.59, 0.11)), 0.0001);
}

// Reversed Z: a depth farther than another. What's in front of a cut-out never is; a neighbour of the same surface may be,
// by its slope, and blends with it as much as it's the same.
fn farther(depth: f32, than: f32) -> bool {
    return depth < than;
}

@fragment
fn fs_sub_sample_alpha(@builtin(position) position: vec4f) -> @location(0) vec4f {
    let pixel = vec2i(position.xy);
    let last = vec2i(textureDimensions(scene_copy)) - 1;
    let here = textureLoad(scene_copy, pixel, 0);
    let depth = textureLoad(scene_depth, pixel, 0);
    // The dropping half (rdr2_cutout.wgsl's rdr2_sub_sample_half) shows what's behind the cut-out; the keeping half is it.
    let dropping = fract(dot(position.xy, vec2f(0.5))) < 0.5;
    // Right first, as the game pairs them.
    var around = array<vec2i, 4>(vec2i(1, 0), vec2i(-1, 0), vec2i(0, 1), vec2i(0, -1));
    var behind = here;
    var front = here;
    var coverage = marked_coverage(here);
    if (dropping) {
        // The kept pixels around, nearer than this one, the pixel to the right alone where it is one.
        var sum = vec4f(0.0);
        var count = 0.0;
        for (var i = 0; i < 4; i++) {
            let at = pixel + around[i];
            if (any(at < vec2i(0)) || any(at > last)) {
                continue;
            }
            let other = textureLoad(scene_copy, at, 0);
            let other_coverage = marked_coverage(other);
            if (other_coverage > 0.0 && farther(depth, textureLoad(scene_depth, at, 0))) {
                sum += vec4f(other.rgb, other_coverage);
                count += 1.0;
                if (i == 0) {
                    break;
                }
            }
        }
        if (count == 0.0) {
            discard;
        }
        front = vec4f(sum.rgb / count, 1.0);
        coverage = sum.a / count;
    } else {
        if (coverage <= 0.0) {
            discard;
        }
        // The dropped pixels around, farther than this one, the pixel to the right alone where it is one.
        var sum = vec4f(0.0);
        var count = 0.0;
        for (var i = 0; i < 4; i++) {
            let at = pixel + around[i];
            if (any(at < vec2i(0)) || any(at > last)) {
                continue;
            }
            if (farther(textureLoad(scene_depth, at, 0), depth)) {
                sum += textureLoad(scene_copy, at, 0);
                count += 1.0;
                if (i == 0) {
                    break;
                }
            }
        }
        // Nothing behind it seen around: as it is, its coverage whole.
        if (count == 0.0) {
            return vec4f(here.rgb, 1.0);
        }
        behind = sum / count;
    }
    // The front's alpha is the mark, its coverage whole; what's behind it is never marked.
    if (behind.a < 1.0) {
        return vec4f(mix(behind.rgb, front.rgb, coverage), mix(behind.a, 1.0, coverage));
    }
    let behind_luminance = luminance(behind.rgb);
    let front_luminance = luminance(front.rgb);
    let chroma = mix(behind.rgb / behind_luminance, front.rgb / front_luminance, coverage);
    let mixed = exp(mix(log(0.4 * behind_luminance), log(0.4 * front_luminance), coverage)) * 2.5;
    return vec4f(chroma * mixed, 1.0);
}
