// Collision and navmesh triangles: flat faces in their material's colour, lit so their shape reads, with their
// edges drawn faintly so the triangles themselves show (not where a polygon was split into triangles: its outline
// is drawn as lines instead). Every triangle has corners of its own, so a corner's place
// in its triangle (vertex_index % 3) gives the barycentric coordinates the edges are found with.
// Scene, the colour helpers and the dither come from scene.wgsl.

struct MeshInput {
    @builtin(vertex_index) index: u32,
    @location(0) position: vec3f,
    @location(1) normal: vec3f,
    // sRGB, as the material lists it; alpha is how strongly the triangle's own edges show.
    @location(2) colour: vec4f,
};

struct MeshOutput {
    @builtin(position) position: vec4f,
    @location(0) normal: vec3f,
    @location(1) colour: vec3f,
    @location(2) barycentric: vec3f,
    @location(3) edges: f32,
};

@vertex
fn vs_mesh(input: MeshInput) -> MeshOutput {
    var corners = array<vec3f, 3>(vec3f(1.0, 0.0, 0.0), vec3f(0.0, 1.0, 0.0), vec3f(0.0, 0.0, 1.0));
    var output: MeshOutput;
    output.position = scene.view_projection * vec4f(input.position, 1.0);
    output.normal = input.normal;
    output.colour = srgb_to_linear(input.colour.rgb);
    output.barycentric = corners[input.index % 3u];
    output.edges = input.colour.a;
    return output;
}

// A sun high up and to one side, so slopes and walls read apart.
const SUN: vec3f = vec3f(0.36, 0.24, 0.9);

@fragment
fn fs_mesh(input: MeshOutput, @builtin(front_facing) front: bool) -> @location(0) vec4f {
    // Collisions aren't wound consistently: either side of a face is lit as its front.
    let normal = select(-input.normal, input.normal, front);
    let sun = max(dot(normal, normalize(SUN)), 0.0);
    // A light from the camera keeps faces turned away from the sun from going flat.
    let head = max(dot(normal, -scene.forward.xyz), 0.0);
    var colour = input.colour * (0.22 + 0.5 * sun + 0.38 * head);

    // The triangle's edges, about a pixel wide whatever its size on screen.
    let width = fwidth(input.barycentric);
    let inside = smoothstep(vec3f(0.0), width * 1.2, input.barycentric);
    let edge = 1.0 - min(min(inside.x, inside.y), inside.z);
    colour = mix(colour, colour * 0.45, edge * 0.7 * input.edges);

    return vec4f(colour, 1.0);
}
