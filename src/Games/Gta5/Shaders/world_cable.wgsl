// GTA V's cables in its world: Gta5/cable.wgsl's wire, each placement's moved by its matrix first, then taken through the
// world's fog as VS_Cable and PS_Cable take it: fainter by how much fog is in front of it (before the alpha test, which
// the game does in its vertex shader, here at the pixel), and its colour fogged.
// Put after Scene/scene.wgsl, Scene/light.wgsl, Gta5/cable.wgsl, World/world_instances.wgsl, Gta5/fog.wgsl and
// Gta5/world_fog.wgsl.

@vertex
fn vs_cable_world(input: CableInput, @location(8) source_index: u32) -> CableOutput {
    let instance = world_instances[source_index];
    let model = instance_model(instance);
    let p = (model * vec4f(input.position, 1.0)).xyz - scene.origin.xyz;
    let direction = (model * vec4f(input.normal, 0.0)).xyz;
    return cable_vertex(p, direction, input.uv0, input.colour0);
}

@fragment
fn fs_cable_world(original: CableOutput) -> @location(0) vec4f {
    var input = original;
    let eye = scene.eye.xyz - scene.origin.xyz;
    input.colour.a *= 1.0 - gta_fog(world_fog.fog, input.place - eye, 1.0).a;
    return gta_world_fogged(cable_colour(input), input.place, input.position.xy);
}
