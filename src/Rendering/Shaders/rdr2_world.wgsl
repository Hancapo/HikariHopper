// Red Dead Redemption 2's world: each streamed asset's vertices drawn once per placement, as RedFury's vs_world does.
// A placement's world matrix and its inverse transpose stay on the GPU for as long as it's covered; each frame only the
// four-byte indexes of the placements in view move, read per instance. Positions go relative to the scene's origin, the
// camera, so the world's large coordinates stay precise. Grass shrinks away with distance and carries its colour and
// occlusion. Put after scene.wgsl, light.wgsl, rdr2_light.wgsl,
// rdr2_terrain_blending.wgsl, rdr2_material.wgsl, rdr2_vertex.wgsl and world_instances.wgsl.

// A plant shrinks to nothing between its fade's start and end, from the camera.
fn grass_fade(instance: WorldInstance) -> f32 {
    return grass_fade_from(instance, scene.origin.xyz);
}

fn place_instance(input: MaterialVertexInput, instance: WorldInstance) -> MaterialVertexInput {
    let model = instance_model(instance);
    var placed = input;
    placed.position = (model * vec4f(input.position * grass_fade(instance), 1.0)).xyz;
    placed.normal = (instance_normal_matrix(instance) * vec4f(input.normal, 0.0)).xyz;
    placed.tangent = vec4f((model * vec4f(input.tangent.xyz, 0.0)).xyz, input.tangent.w * instance_handedness(instance));
    return placed;
}

@vertex
fn vs_rdr2_world(vertex: Rdr2Vertex, @location(8) source_index: u32) -> MaterialVertexOutput {
    let instance = world_instances[source_index];
    // In the world, then relative to the scene's origin (the camera), where the material and the view work.
    var placed = place_instance(rdr2_input(vertex), instance);
    placed.position -= scene.origin.xyz;
    instance_tint_row = instance_tint_row_of(instance);
    var output = material_vertex(placed);
    output.relative_position = placed.position;
    if (is_grass(instance)) {
        output.grass_colour_ao = vec4f(instance.n0.w, instance.n1.w, instance.n2.w, instance.n3.w);
    }
    output.lod_fade = instance_lod_fade(instance);
    output.position = scene.view_projection * vec4f(placed.position, 1.0);
    return output;
}

// What the world's surfaces write: the colour, then what its ambient occlusion takes back out (light.wgsl's occludable).
struct Rdr2WorldTargets {
    @location(0) colour: vec4f,
    @location(2) ambient: vec4f,
    @location(3) normal: vec4f,
};

@fragment
fn fs_rdr2_world(input: MaterialVertexOutput, @builtin(front_facing) front: bool) -> Rdr2WorldTargets {
    use_local_lights(input.position.xy, input.relative_position);
    sub_sampled_view = true;
    // Left out after shading, which needs uniform control flow.
    let colour = shade_material(input, front);
    if (!lod_fade_keeps(input.position.xy, input.lod_fade)) {
        discard;
    }
    return Rdr2WorldTargets(colour, occludable_ambient, occludable_normal);
}
