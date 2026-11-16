// Red Dead Redemption 2's cut-outs in the world's picking: their holes aren't them, what's behind is picked. Put after
// world_pick.wgsl.

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
