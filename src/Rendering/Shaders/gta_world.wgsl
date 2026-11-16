// GTA V's world: each streamed asset's vertices drawn once per placement (rdr2_world.wgsl's way), shaded by
// surface.wgsl as the model viewer shades them. A placement's matrices are read per instance; positions go relative to
// the scene's origin, the camera, so the world's large coordinates stay precise. An entity's tint picks its row of a
// _tnt shader's palette, as the game's tint palette selector. Put after scene.wgsl, light.wgsl, surface.wgsl and
// world_instances.wgsl.

// A placement's vertex: in the world, then relative to the scene's origin.
fn place_surface(input: SurfaceInput, instance: WorldInstance) -> SurfaceInput {
    let model = instance_model(instance);
    var placed = input;
    placed.position = (model * vec4f(input.position, 1.0)).xyz - scene.origin.xyz;
    placed.normal = (instance_normal_matrix(instance) * vec4f(input.normal, 0.0)).xyz;
    placed.tangent = vec4f((model * vec4f(input.tangent.xyz, 0.0)).xyz, input.tangent.w * instance_handedness(instance));
    return placed;
}

@vertex
fn vs_surface_world(input: SurfaceInput, @location(8) source_index: u32) -> SurfaceOutput {
    let instance = world_instances[source_index];
    let placed = place_surface(input, instance);
    var output = surface_vertex(placed);
    let row = instance_tint_row_of(instance);
    if (material.extra.y > 0.5 && row > 0) {
        let rows = f32(textureDimensions(tint_palette).y);
        output.tint = textureSampleLevel(tint_palette, palette_sampler, vec2f(placed.colour0.b, (f32(row) + 0.5) / rows), 0.0).rgb;
    }
    output.position = scene.view_projection * vec4f(placed.position, 1.0);
    return output;
}
