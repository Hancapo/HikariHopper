// GTA V's world's terrain: Gta5/world.wgsl's placement, shaded by Gta5/terrain.wgsl. Put after Scene/scene.wgsl, Scene/light.wgsl,
// Gta5/terrain.wgsl, World/world_instances.wgsl, Gta5/fog.wgsl and Gta5/world_fog.wgsl.

@vertex
fn vs_terrain_world(input: TerrainInput, @location(8) source_index: u32) -> TerrainOutput {
    let instance = world_instances[source_index];
    let model = instance_model(instance);
    var placed = input;
    placed.position = (model * vec4f(input.position, 1.0)).xyz - scene.origin.xyz;
    placed.normal = (instance_normal_matrix(instance) * vec4f(input.normal, 0.0)).xyz;
    placed.tangent = vec4f((model * vec4f(input.tangent.xyz, 0.0)).xyz, input.tangent.w * instance_handedness(instance));
    var output = terrain_vertex(placed);
    let row = instance_tint_row_of(instance);
    if (terrain.options.z > 0.5 && row > 0) {
        let rows = f32(textureDimensions(tint_palette).y);
        output.tint = textureSampleLevel(tint_palette, palette_sampler, vec2f(placed.colour0.b, (f32(row) + 0.5) / rows), 0.0).rgb;
    }
    output.room = instance_room(instance);
    output.lod_fade = instance_lod_fade(instance);
    output.position = scene.view_projection * vec4f(placed.position, 1.0);
    return output;
}

// A blended piece of terrain, drawn after the world's fog pass: it takes the fog itself (Gta5/world.wgsl's
// fs_surface_world_fogged).
@fragment
fn fs_terrain_world_fogged(input: TerrainOutput) -> @location(0) vec4f {
    use_local_lights(input.position.xy, input.place);
    use_room(input.room);
    // Left out after shading, which needs uniform control flow.
    let colour = shade_terrain(input);
    if (!lod_fade_keeps(input.position.xy, input.lod_fade)) {
        discard;
    }
    return gta_world_fogged(colour, input.place, input.position.xy);
}
