// The geometries picked in the viewer, outlined over the frame as a 3D tool outlines its selection: first their shape
// is drawn into a mask, all of it, seen through whatever is in front; then, across the frame, a pixel just outside the
// mask takes the outline's colour, and the inside is left as it is drawn.
// Scene, the colour helpers and the dither come from scene.wgsl.

// The app's accent (Theme.Accent, #5E8BD0), in sRGB.
const OUTLINE: vec3f = vec3f(0.369, 0.545, 0.816);
// How far out from the shape the outline reaches, in pixels.
const REACH: i32 = 2;

@vertex
fn vs_mask(@location(0) position: vec3f) -> @builtin(position) vec4f {
    return scene.view_projection * vec4f(position, 1.0);
}

@fragment
fn fs_mask() -> @location(0) vec4f {
    return vec4f(1.0, 0.0, 0.0, 1.0);
}

@group(1) @binding(0) var mask: texture_2d<f32>;

// One triangle over the whole frame.
@vertex
fn vs_outline(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
    let corner = vec2f(f32((index << 1u) & 2u), f32(index & 2u));
    return vec4f(corner * 2.0 - 1.0, 0.0, 1.0);
}

@fragment
fn fs_outline(@builtin(position) position: vec4f) -> @location(0) vec4f {
    let size = vec2i(textureDimensions(mask));
    let pixel = vec2i(position.xy);
    let colour = srgb_to_linear(OUTLINE);
    // Inside the shape: nothing over it, so the picked geometry shows as it is drawn.
    if (textureLoad(mask, pixel, 0).r > 0.5) {
        discard;
    }
    for (var dy = -REACH; dy <= REACH; dy++) {
        for (var dx = -REACH; dx <= REACH; dx++) {
            if (dx * dx + dy * dy > REACH * REACH + 1) {
                continue;
            }
            let near = clamp(pixel + vec2i(dx, dy), vec2i(0), size - 1);
            if (textureLoad(mask, near, 0).r > 0.5) {
                return vec4f(dither(colour, position.xy), 1.0);
            }
        }
    }
    discard;
    return vec4f(0.0);
}
