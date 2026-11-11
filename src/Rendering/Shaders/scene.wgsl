// What every viewport shader shares: the scene's uniforms and the sRGB helpers. WGSL has no includes, so this file
// is put in front of each shader as it is loaded (ViewportRenderer.Shader).
//
// Meshes are drawn relative to `origin`, the middle of what is framed, so content placed far out in the world stays
// precise; the backdrop works in world space. Depth is infinite reversed Z: one at the near plane, zero at infinity.

struct Scene {
    // From origin-relative positions to clip space.
    view_projection: mat4x4<f32>,
    // The camera, in world space.
    eye: vec4f,
    forward: vec4f,
    right: vec4f,
    up: vec4f,
    // Aspect ratio, tan(fov / 2), ground height, minor grid spacing.
    projection: vec4f,
    // The backdrop's colours, linear; alpha is how strongly each line shows.
    zenith: vec4f,
    horizon: vec4f,
    void_colour: vec4f,
    grid_minor: vec4f,
    grid_major: vec4f,
    axis_x: vec4f,
    axis_y: vec4f,
    // The world position meshes are drawn relative to.
    origin: vec4f,
    // The viewport's width and height in pixels.
    viewport: vec4f,
};

@group(0) @binding(0) var<uniform> scene: Scene;

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

// A linear colour for an 8-bit sRGB target, dithered in display space so gradients don't band. The viewport's frame is
// half-float and dithered once, as it goes to the window (present.wgsl), so its own passes don't need it.
fn dither(colour: vec3f, pixel: vec2f) -> vec3f {
    let noise = (interleaved_gradient_noise(pixel) - 0.5) / 255.0;
    return srgb_to_linear(clamp(linear_to_srgb(colour) + noise, vec3f(0.0), vec3f(1.0)));
}
