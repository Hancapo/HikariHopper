// GTA V's cut-outs into the sun's shadow, where their diffuse alpha passes the cutoff, as shadow.wgsl's
// fs_shadow_masked has them. Put after world_shadow.wgsl.

// The start of surface.wgsl's Material: the alpha cutoff is extra.x.
struct ShadowMaterial {
    specular: vec4f,
    mask: vec4f,
    options: vec4f,
    extra: vec4f,
};

@group(1) @binding(0) var<uniform> material: ShadowMaterial;
@group(1) @binding(1) var surface_sampler: sampler;
@group(1) @binding(2) var diffuse_map: texture_2d<f32>;

@fragment
fn fs_world_shadow_masked(input: MaskedOutput) {
    if (textureSample(diffuse_map, surface_sampler, input.uv).a < min(material.extra.x, 128.5 / 255.0)) {
        discard;
    }
}
