// Red Dead Redemption 2's cut-outs, as the game draws them on PC without MSAA: with sub-sampled alpha, its
// _SubSampleAlpha pixel programs (standard's PS_DeferredTextured_SubSampleAlphaClip, the trees'
// PS_PropFoliageDeferredSubSampleAlpha), in the form they take with the game's TAA off, which HikariHopper doesn't have:
//   - on half the pixels, a checkerboard, a pixel the surface only partly covers is dropped; on the other half it is kept,
//     and marks the frame with its coverage, which rdr2_subsample_alpha.wgsl's pass (the game's PS_SubSampleAlphaSinglePass)
//     then blends with the pixel to its right, behind it, by that coverage;
//   - the standard programs' coverage is the alpha, and a pixel is dropped on both halves at or under a 255th; the trees'
//     ramps from nought at an alpha of 0.1 to whole at 0.75, their pixels dropped on both halves at or under 0.1.
// Grass keeps its own alpha test, as the game's grass_batch programs do (they have no sub-sampled alpha), and so do
// what's drawn into the reflection map, as the game's ReflectionGBufferAlphaClip programs. The cascades cut a surface
// out where twice its alpha (the trees') or its alpha (the others') is under 90/255, as the game's
// PS_CascadeShadows_texture. Rdr2Material sets the flags. Put before what reads it.

// rdr2_material.wgsl's flags.w: drawn with sub-sampled alpha, and with the trees' programs.
const RDR2_SUB_SAMPLED: u32 = 256u;
const RDR2_TREE_CUTOUT: u32 = 512u;

fn rdr2_sub_sampled(flags: u32) -> bool {
    return (flags & RDR2_SUB_SAMPLED) != 0u;
}

// How much of a pixel the surface covers, from its alpha.
fn rdr2_cutout_coverage(alpha: f32, flags: u32) -> f32 {
    if ((flags & RDR2_TREE_CUTOUT) != 0u) {
        return saturate((alpha - 0.1) * 1.538462 + 1.0 / 255.0);
    }
    return alpha;
}

// The checkerboard's dropping half: where x + y is odd, as the game's frac((x + y) / 2) < 0.5 of a pixel's centre.
fn rdr2_sub_sample_half(pixel: vec2f) -> bool {
    return fract(dot(pixel, vec2f(0.5))) < 0.5;
}

// Whether the sub-sampled surface leaves a pixel out.
fn rdr2_sub_sample_drops(pixel: vec2f, alpha: f32, flags: u32) -> bool {
    if ((flags & RDR2_TREE_CUTOUT) != 0u) {
        return (rdr2_sub_sample_half(pixel) && alpha < 0.75) || alpha <= 0.1;
    }
    return (rdr2_sub_sample_half(pixel) && alpha < 1.0) || alpha <= 1.0 / 255.0;
}

// What a pixel the surface keeps writes as its alpha: its coverage, over 2 where it's partial, for the blend after (the
// frame's alpha is otherwise its coverage of the backdrop, nought to one); whole, one.
fn rdr2_sub_sample_mark(coverage: f32) -> f32 {
    return select(1.0, 2.0 + coverage, coverage < 1.0);
}

// Whether a cut-out casts into the cascades where its alpha is this.
fn rdr2_cutout_casts(alpha: f32, flags: u32, cutoff: f32) -> bool {
    if ((flags & RDR2_TREE_CUTOUT) != 0u) {
        return 2.0 * alpha - 90.0 / 255.0 >= 0.0;
    }
    if (rdr2_sub_sampled(flags)) {
        return alpha - 90.0 / 255.0 >= 0.0;
    }
    return alpha >= cutoff;
}

// Whether a cut-out is picked where its alpha is this: where it covers at least half the pixel, as drawn.
fn rdr2_cutout_picks(alpha: f32, flags: u32, cutoff: f32) -> bool {
    if (rdr2_sub_sampled(flags)) {
        return rdr2_cutout_coverage(alpha, flags) >= 0.5;
    }
    return alpha >= cutoff;
}
