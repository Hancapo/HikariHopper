// What's under the pointer in Red Dead Redemption 2's world, and the outline of what's picked: each placement drawn into
// an id target (its asset's number and its own), nearest first, cut-outs where their alpha keeps them; and one
// placement's shape into outline.wgsl's mask. Put after scene.wgsl.

struct WorldInstance {
    m0: vec4f, m1: vec4f, m2: vec4f, m3: vec4f,
    n0: vec4f, n1: vec4f, n2: vec4f, n3: vec4f,
};

@group(3) @binding(0) var<storage, read> world_instances: array<WorldInstance>;

// The start of rdr2_material.wgsl's MaterialUniforms, as rdr2_world_shadow.wgsl reads it, for the cut-outs' test.
struct Rdr2PickMaterial {
    base_colour_factor: vec4f,
    surface: vec4f,
    emission_alpha_layers: vec4f,
    decal_opacity: vec4f,
    flags: vec4u,
};

@group(1) @binding(0) var<uniform> material: Rdr2PickMaterial;
@group(1) @binding(1) var material_sampler: sampler;
@group(1) @binding(2) var base_texture: texture_2d<f32>;

// The asset being drawn: its number, from 1 (0 is nothing).
struct PickAsset {
    id: vec4u,
};

@group(2) @binding(0) var<uniform> pick_asset: PickAsset;

// A placement's vertex relative to the camera; grass isn't picked, so it's drawn as nothing.
fn placed(position: vec3f, source_index: u32) -> vec4f {
    let instance = world_instances[source_index];
    if (instance.n3.z > 0.5) {
        return vec4f(0.0, 0.0, 0.0, 1.0);
    }
    let model = mat4x4f(instance.m0, instance.m1, instance.m2, instance.m3);
    let world = (model * vec4f(position, 1.0)).xyz - scene.origin.xyz;
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

// A cut-out's holes aren't it: what's behind them is picked.
@fragment
fn fs_world_pick_masked(input: PickOutput) -> @location(0) vec2u {
    var alpha = textureSample(base_texture, material_sampler, input.uv).a * material.base_colour_factor.a;
    if (((material.flags.w >> 3u) & 3u) == 2u) {
        alpha *= input.alpha;
    }
    if (alpha < material.emission_alpha_layers.y) {
        discard;
    }
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
