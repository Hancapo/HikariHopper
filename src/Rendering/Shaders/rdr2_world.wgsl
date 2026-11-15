// Red Dead Redemption 2's world: each streamed asset's vertices drawn once per placement, as RedFury's vs_world does.
// A placement's world matrix and its inverse transpose stay on the GPU for as long as it's covered; each frame only the
// four-byte indexes of the placements in view move, read per instance. Positions go relative to the scene's origin, the
// camera, so the world's large coordinates stay precise. Put after scene.wgsl, light.wgsl, rdr2_light.wgsl,
// rdr2_terrain_blending.wgsl, rdr2_material.wgsl and rdr2_vertex.wgsl.

struct WorldInstance {
    m0: vec4f, m1: vec4f, m2: vec4f, m3: vec4f,
    n0: vec4f, n1: vec4f, n2: vec4f, n3: vec4f,
};

// The asset's placements: the world matrix, from the asset's own space, then its inverse transpose, whose n3.x carries
// the placement's tint palette row (0 keeps the material's).
@group(3) @binding(0) var<storage, read> world_instances: array<WorldInstance>;

fn place_instance(input: MaterialVertexInput, instance: WorldInstance) -> MaterialVertexInput {
    let model = mat4x4f(instance.m0, instance.m1, instance.m2, instance.m3);
    let normal_matrix = mat4x4f(instance.n0, instance.n1, instance.n2, vec4f(0.0, 0.0, 0.0, 1.0));
    var placed = input;
    placed.position = (model * vec4f(input.position, 1.0)).xyz;
    placed.normal = (normal_matrix * vec4f(input.normal, 0.0)).xyz;
    let handedness = sign(determinant(mat3x3f(instance.m0.xyz, instance.m1.xyz, instance.m2.xyz)));
    placed.tangent = vec4f((model * vec4f(input.tangent.xyz, 0.0)).xyz, input.tangent.w * handedness);
    return placed;
}

@vertex
fn vs_rdr2_world(vertex: Rdr2Vertex, @location(8) source_index: u32) -> MaterialVertexOutput {
    let instance = world_instances[source_index];
    // In the world, then relative to the scene's origin (the camera), where the material and the view work.
    var placed = place_instance(rdr2_input(vertex), instance);
    placed.position -= scene.origin.xyz;
    instance_tint_row = i32(round(instance.n3.x));
    var output = material_vertex(placed);
    output.relative_position = placed.position;
    output.position = scene.view_projection * vec4f(placed.position, 1.0);
    return output;
}

@fragment
fn fs_rdr2_world(input: MaterialVertexOutput, @builtin(front_facing) front: bool) -> @location(0) vec4f {
    return shade_material(input, front);
}
