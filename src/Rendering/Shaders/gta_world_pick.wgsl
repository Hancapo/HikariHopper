// GTA V's cut-outs in the world's picking: their holes aren't them, what's behind is picked, by shadow.wgsl's test.
// Put after world_pick.wgsl.

// The start of surface.wgsl's Material: the alpha cutoff is extra.x.
struct GtaPickMaterial {
    specular: vec4f,
    mask: vec4f,
    options: vec4f,
    extra: vec4f,
};

@group(1) @binding(0) var<uniform> material: GtaPickMaterial;
@group(1) @binding(1) var surface_sampler: sampler;
@group(1) @binding(2) var diffuse_map: texture_2d<f32>;

@fragment
fn fs_world_pick_masked(input: PickOutput) -> @location(0) vec2u {
    if (textureSample(diffuse_map, surface_sampler, input.uv).a < min(material.extra.x, 128.5 / 255.0)) {
        discard;
    }
    return vec2u(pick_asset.id.x, input.source);
}
