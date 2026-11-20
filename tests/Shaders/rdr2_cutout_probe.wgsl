// A test's stand-in for what the HDR pass draws before RDR2's sub-sampled alpha is put together: a rectangle of one
// colour at one depth, either solid or a cut-out kept and marked on the checkerboard as rdr2_material.wgsl's
// fs_main does it, by rdr2_cutout.wgsl's own functions (put before this).

struct Probe {
    colour: vec4f,
    // The pixels it covers: x0, y0, x1, y1, the far ones excluded.
    rect: vec4f,
    depth: f32,
    alpha: f32,
    flags: u32,
    // 0 a solid surface, 1 a sub-sampled cut-out.
    cutout: u32,
}

@group(0) @binding(0) var<uniform> probe: Probe;

@vertex
fn vs_probe(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
    let corner = vec2f(f32((index << 1u) & 2u), f32(index & 2u));
    return vec4f(corner * 2.0 - 1.0, probe.depth, 1.0);
}

@fragment
fn fs_probe(@builtin(position) position: vec4f) -> @location(0) vec4f {
    if (any(position.xy < probe.rect.xy) || any(position.xy >= probe.rect.zw)) {
        discard;
    }
    if (probe.cutout == 0u) {
        return vec4f(probe.colour.rgb, 1.0);
    }
    if (rdr2_sub_sample_drops(position.xy, probe.alpha, probe.flags)) {
        discard;
    }
    return vec4f(probe.colour.rgb, rdr2_sub_sample_mark(rdr2_cutout_coverage(probe.alpha, probe.flags)));
}
