// Lines over the meshes (a navmesh's outlines, its climbs and drops, portals and cover points), each segment drawn
// as a quad its width in pixels at any distance: WebGPU's own lines are one pixel wide. One instance per segment;
// its six corners are two triangles across the segment's two ends.
// Scene, the colour helpers and the dither come from scene.wgsl.

struct Segment {
    @builtin(vertex_index) index: u32,
    @location(0) a: vec3f,
    @location(1) b: vec3f,
    // sRGB.
    @location(2) colour: vec4f,
    // In pixels.
    @location(3) width: f32,
};

struct LineOutput {
    @builtin(position) position: vec4f,
    @location(0) colour: vec3f,
};

// Nearer than this, in view distance, an end is behind the camera.
const NEAR: f32 = 1e-4;

@vertex
fn vs_line(input: Segment) -> LineOutput {
    var a = scene.view_projection * vec4f(input.a, 1.0);
    var b = scene.view_projection * vec4f(input.b, 1.0);
    var output: LineOutput;
    output.colour = srgb_to_linear(input.colour.rgb);
    if (a.w < NEAR && b.w < NEAR) {
        // Wholly behind the camera: nothing to draw.
        output.position = vec4f(2.0, 2.0, 0.0, 1.0);
        return output;
    }
    // A segment through the camera is cut where it passes it.
    if (a.w < NEAR) {
        a = mix(a, b, (NEAR - a.w) / (b.w - a.w));
    } else if (b.w < NEAR) {
        b = mix(b, a, (NEAR - b.w) / (a.w - b.w));
    }

    let half_size = scene.viewport.xy * 0.5;
    let along = b.xy / b.w * half_size - a.xy / a.w * half_size;
    let direction = select(normalize(along), vec2f(1.0, 0.0), dot(along, along) < 1e-8);
    let across = vec2f(-direction.y, direction.x);

    var ends = array<f32, 6>(0.0, 1.0, 0.0, 0.0, 1.0, 1.0);
    var sides = array<f32, 6>(-1.0, -1.0, 1.0, 1.0, -1.0, 1.0);
    let clip = select(a, b, ends[input.index] > 0.5);
    // Half the width either side, in pixels, back into clip space at this end's depth.
    let offset = across * sides[input.index] * input.width * 0.5 / half_size * clip.w;
    output.position = vec4f(clip.xy + offset, clip.z, clip.w);
    return output;
}

@fragment
fn fs_line(input: LineOutput) -> @location(0) vec4f {
    return vec4f(dither(input.colour, input.position.xy), 1.0);
}
