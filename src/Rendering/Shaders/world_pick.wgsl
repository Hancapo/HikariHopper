// What's under the pointer in a game's world, and the outline of what's picked: each placement drawn into an id target
// (its asset's number and its own), nearest first, cut-outs where their game's alpha test keeps them
// (Rdr2/world_pick.wgsl, Gta5/world_pick.wgsl, put after this); and one placement's shape into outline.wgsl's mask. Put
// after scene.wgsl and world_instances.wgsl.

// The asset being drawn: its number, from 1 (0 is nothing).
struct PickAsset {
    id: vec4u,
};

@group(2) @binding(0) var<uniform> pick_asset: PickAsset;

// A placement's vertex relative to the camera; grass isn't picked, so it's drawn as nothing.
fn placed(position: vec3f, source_index: u32) -> vec4f {
    let instance = world_instances[source_index];
    if (is_grass(instance)) {
        return vec4f(0.0, 0.0, 0.0, 1.0);
    }
    let world = (instance_model(instance) * vec4f(position, 1.0)).xyz - scene.origin.xyz;
    return scene.view_projection * vec4f(world, 1.0);
}

struct PickOutput {
    @builtin(position) position: vec4f,
    @location(0) uv: vec2f,
    @location(1) alpha: f32,
    @location(2) @interpolate(flat) source: u32,
};

@vertex
fn vs_world_pick(@location(0) position: vec3f, @location(3) uv: vec2f, @location(5) colour: vec4f,
    @location(8) source_index: u32) -> PickOutput {
    var output: PickOutput;
    output.position = placed(position, source_index);
    output.uv = uv;
    output.alpha = colour.a;
    output.source = source_index;
    return output;
}

@fragment
fn fs_world_pick(input: PickOutput) -> @location(0) vec2u {
    return vec2u(pick_asset.id.x, input.source);
}

// The picked placement's shape, all of it, into the outline's mask.
@vertex
fn vs_world_mask(@location(0) position: vec3f, @location(8) source_index: u32) -> @builtin(position) vec4f {
    return placed(position, source_index);
}

@fragment
fn fs_world_mask() -> @location(0) vec4f {
    return vec4f(1.0, 0.0, 0.0, 1.0);
}
