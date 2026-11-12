// The frame to the window (ViewportRenderer.Antialiasing.cs), with CMAA2's anti-aliased pixels over it (cmaa2.wgsl):
// both linear and at half-float precision, for the window's 8-bit sRGB view. This is where the frame is rounded to 8
// bits, once, so it is dithered here: everything blended into it (a model's glow over the sky, say) stays smooth.

@vertex
fn vs_present(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
    let corner = vec2f(f32((index << 1u) & 2u), f32(index & 2u));
    return vec4f(corner * 2.0 - 1.0, 0.0, 1.0);
}

@group(0) @binding(0) var present_frame: texture_2d<f32>;
@group(0) @binding(1) var present_blended: texture_2d<f32>;

fn linear_to_srgb(c: vec3f) -> vec3f {
    return select(1.055 * pow(c, vec3f(1.0 / 2.4)) - 0.055, c * 12.92, c <= vec3f(0.0031308));
}

fn srgb_to_linear(c: vec3f) -> vec3f {
    return select(pow((c + 0.055) / 1.055, vec3f(2.4)), c / 12.92, c <= vec3f(0.04045));
}

// Jimenez's interleaved gradient noise: even, and invisible at one 8-bit step.
fn interleaved_gradient_noise(pixel: vec2f) -> f32 {
    return fract(52.9829189 * fract(dot(pixel, vec2f(0.06711056, 0.00583715))));
}

// A linear colour for the 8-bit sRGB window, dithered by under one step in display space so gradients don't band.
fn dither(colour: vec3f, pixel: vec2f) -> vec3f {
    let noise = (interleaved_gradient_noise(pixel) - 0.5) / 255.0;
    return srgb_to_linear(clamp(linear_to_srgb(max(colour, vec3f(0.0))) + noise, vec3f(0.0), vec3f(1.0)));
}

@fragment
fn fs_present(@builtin(position) frag: vec4f) -> @location(0) vec4f {
    let pixel = vec2i(frag.xy);
    let blended = textureLoad(present_blended, pixel, 0);
    let colour = select(textureLoad(present_frame, pixel, 0).rgb, blended.rgb, blended.a > 0.5);
    return vec4f(dither(colour, frag.xy), 1.0);
}
