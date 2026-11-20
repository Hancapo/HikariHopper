// The sun's shadow of a game's world (after RedFury's vs_world_shadow): each streamed asset's casters drawn once per
// placement into a cascade, their depth alone; cut-outs through their game's alpha test (Rdr2/world_shadow.wgsl,
// Gta5/world_shadow.wgsl, put after this). Standalone: the cascade's view, and the asset's placements
// (world_instances.wgsl, put before this).

struct ShadowView {
    // From the world to the cascade's clip space: the cascade's view, after the move to the camera it was fitted for.
    matrix: mat4x4f,
    // That camera, where a plant's fade is measured from.
    origin: vec4f,
};

@group(0) @binding(0) var<uniform> shadow_view: ShadowView;

fn caster_position(position: vec3f, source_index: u32) -> vec4f {
    let instance = world_instances[source_index];
    let fade = grass_fade_from(instance, shadow_view.origin.xyz);
    return shadow_view.matrix * (instance_model(instance) * vec4f(position * fade, 1.0));
}

// Solid casters: depth alone, no fragment stage.
@vertex
fn vs_world_shadow(@location(0) position: vec3f, @location(8) source_index: u32) -> @builtin(position) vec4f {
    return caster_position(position, source_index);
}

struct MaskedOutput {
    @builtin(position) position: vec4f,
    @location(0) uv: vec2f,
    @location(1) alpha: f32,
};

@vertex
fn vs_world_shadow_masked(@location(0) position: vec3f, @location(3) uv: vec2f, @location(5) colour: vec4f,
    @location(8) source_index: u32) -> MaskedOutput {
    var output: MaskedOutput;
    output.position = caster_position(position, source_index);
    output.uv = uv;
    output.alpha = colour.a;
    return output;
}
