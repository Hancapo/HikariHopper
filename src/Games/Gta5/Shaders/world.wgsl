// GTA V's world: each streamed asset's vertices drawn once per placement (Rdr2/world.wgsl's way), shaded by
// Gta5/surface.wgsl as the model viewer shades them, a tree's moved first by its vertex shader's work (Gta5/trees.wgsl). A
// placement's matrices are read per instance; positions go relative to the scene's origin, the camera, so the world's
// large coordinates stay precise. An entity's tint picks its row of a _tnt shader's palette, as the game's tint
// palette selector. Put after scene.wgsl, light.wgsl, Gta5/trees.wgsl, Gta5/surface.wgsl, world_instances.wgsl, Gta5/fog.wgsl and
// Gta5/world_fog.wgsl.

// A placement's vertex: in the world, then relative to the scene's origin. A plant shrinks away with distance.
fn place_surface(input: SurfaceInput, instance: WorldInstance) -> SurfaceInput {
    let model = instance_model(instance);
    var placed = input;
    placed.position = (model * vec4f(input.position * grass_fade_from(instance, scene.origin.xyz), 1.0)).xyz - scene.origin.xyz;
    placed.normal = (instance_normal_matrix(instance) * vec4f(input.normal, 0.0)).xyz;
    placed.tangent = vec4f((model * vec4f(input.tangent.xyz, 0.0)).xyz, input.tangent.w * instance_handedness(instance));
    return placed;
}

@vertex
fn vs_surface_world(original: SurfaceInput, @location(8) source_index: u32) -> SurfaceOutput {
    let instance = world_instances[source_index];
    // A tree's vertex shader works in its model's space, before the placement's matrix; a billboard's normal is the
    // world's as it leaves it.
    let input = tree_input(original, mat3x3f(instance.m0.xyz, instance.m1.xyz, instance.m2.xyz));
    var placed = place_surface(input, instance);
    if (material.tree.x > 2.5) {
        placed.normal = input.normal;
    }
    var output = surface_vertex(placed);
    let row = instance_tint_row_of(instance);
    if (material.extra.y > 0.5 && row > 0) {
        let rows = f32(textureDimensions(tint_palette).y);
        let tint = textureSampleLevel(tint_palette, palette_sampler, vec2f(tint_column(placed), (f32(row) + 0.5) / rows), 0.0);
        output.tint = tint.rgb;
        output.tint_alpha = tint.a;
    }
    // A plant's colour over its diffuse, and its occlusion as its natural ambient (grass_batch's vertex colours are its
    // wind's, not light).
    if (is_grass(instance)) {
        output.tint = vec3f(instance.n0.w, instance.n1.w, instance.n2.w);
        output.colour0 = vec4f(instance.n3.w, 0.0, 0.0, 1.0);
    }
    output.room = instance_room(instance);
    output.lod_fade = instance_lod_fade(instance);
    output.position = scene.view_projection * vec4f(placed.position, 1.0) * day_night_vertex_scale();
    return output;
}

// A blended surface of the world, drawn after the world's fog pass: it takes the fog itself, as the game's forward
// pass does (Gta5/world_fog.wgsl).
@fragment
fn fs_surface_world_fogged(input: SurfaceOutput, @builtin(front_facing) front: bool) -> @location(0) vec4f {
    use_local_lights(input.position.xy, input.place);
    use_room(input.room);
    // Left out after shading, which needs uniform control flow.
    let colour = shade_surface(input, front, false);
    if (!lod_fade_keeps(input.position.xy, input.lod_fade)) {
        discard;
    }
    return gta_world_fogged(colour, input.place, input.position.xy);
}
