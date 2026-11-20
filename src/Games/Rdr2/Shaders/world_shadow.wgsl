// Red Dead Redemption 2's cut-outs into the sun's shadow, where the game's cascades keep them (Rdr2/cutout.wgsl's
// rdr2_cutout_casts). Put after World/world_shadow.wgsl and Rdr2/cutout.wgsl.

// The start of Rdr2/material.wgsl's MaterialUniforms: base_colour_factor, surface, emission_alpha_layers, decal_opacity,
// flags.
struct Rdr2ShadowMaterial {
    base_colour_factor: vec4f,
    surface: vec4f,
    emission_alpha_layers: vec4f,
    decal_opacity: vec4f,
    flags: vec4u,
};

@group(1) @binding(0) var<uniform> material: Rdr2ShadowMaterial;
@group(1) @binding(1) var material_sampler: sampler;
@group(1) @binding(2) var base_texture: texture_2d<f32>;

@fragment
fn fs_world_shadow_masked(input: MaskedOutput) {
    var alpha = textureSample(base_texture, material_sampler, input.uv).a * material.base_colour_factor.a;
    if (((material.flags.w >> 3u) & 3u) == 2u) {
        alpha *= input.alpha;
    }
    if (!rdr2_cutout_casts(alpha, material.flags.w, material.emission_alpha_layers.y)) {
        discard;
    }
}
