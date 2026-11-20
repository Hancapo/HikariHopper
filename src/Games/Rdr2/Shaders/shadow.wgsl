// The key light's shadow of Red Dead Redemption 2's cut-out surfaces, as shadow.wgsl casts GTA V's: their depth where
// the game's cascades keep them (Rdr2/cutout.wgsl's rdr2_cutout_casts). Put after Rdr2/cutout.wgsl: the shadow's view,
// and the start of RDR2's material group.

struct ShadowView {
    matrix: mat4x4f,
};

@group(0) @binding(0) var<uniform> shadow_view: ShadowView;

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

struct MaskedOutput {
    @builtin(position) position: vec4f,
    @location(0) uv: vec2f,
    @location(1) alpha: f32,
};

@vertex
fn vs_rdr2_shadow(@location(0) position: vec3f, @location(3) uv: vec2f, @location(5) colour: vec4f) -> MaskedOutput {
    var output: MaskedOutput;
    output.position = shadow_view.matrix * vec4f(position, 1.0);
    output.uv = uv;
    output.alpha = colour.a;
    return output;
}

@fragment
fn fs_rdr2_shadow(input: MaskedOutput) {
    var alpha = textureSample(base_texture, material_sampler, input.uv).a * material.base_colour_factor.a;
    if (((material.flags.w >> 3u) & 3u) == 2u) {
        alpha *= input.alpha;
    }
    if (!rdr2_cutout_casts(alpha, material.flags.w, material.emission_alpha_layers.y)) {
        discard;
    }
}
