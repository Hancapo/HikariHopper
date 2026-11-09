// The frame to the window (ViewportRenderer.Antialiasing.cs), with CMAA2's anti-aliased pixels over it (cmaa2.wgsl):
// both linear, for the window's sRGB view.

@vertex
fn vs_present(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
    let corner = vec2f(f32((index << 1u) & 2u), f32(index & 2u));
    return vec4f(corner * 2.0 - 1.0, 0.0, 1.0);
}

@group(0) @binding(0) var present_frame: texture_2d<f32>;
@group(0) @binding(1) var present_blended: texture_2d<f32>;

@fragment
fn fs_present(@builtin(position) frag: vec4f) -> @location(0) vec4f {
    let pixel = vec2i(frag.xy);
    let blended = textureLoad(present_blended, pixel, 0);
    if (blended.a > 0.5) {
        return vec4f(blended.rgb, 1.0);
    }
    return vec4f(textureLoad(present_frame, pixel, 0).rgb, 1.0);
}
