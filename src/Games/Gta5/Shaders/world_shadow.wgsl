// GTA V's cut-outs into the sun's shadow, where their diffuse alpha passes the cascades' test, as shadow.wgsl's
// fs_shadow_masked has them; a tree's moved first as the game's tree shaders move them into the cascades, and cut by
// their test. Put after world_shadow.wgsl and Gta5/trees.wgsl.

// Gta5/surface.wgsl's Material, what of it is read: the trees' settings.
struct ShadowMaterial {
    specular: vec4f,
    mask: vec4f,
    options: vec4f,
    extra: vec4f,
    between: array<vec4f, 22>,
    tree: vec4f,
    tree_shade: vec4f,
    tree_size: vec4f,
    tree_normal: vec4f,
};

@group(1) @binding(0) var<uniform> material: ShadowMaterial;
@group(1) @binding(1) var surface_sampler: sampler;
@group(1) @binding(2) var diffuse_map: texture_2d<f32>;

// A placement's cut-out caster: a tree's vertex moved in its model's space, across the cascade's view, then placed.
@vertex
fn vs_gta_world_shadow_masked(@location(0) position: vec3f, @location(2) tangent: vec4f, @location(3) uv: vec2f, @location(4) uv1: vec2f,
    @location(5) colour: vec4f, @location(7) uv2: vec2f, @location(8) source_index: u32) -> MaskedOutput {
    var output: MaskedOutput;
    var place = position;
    output.uv = uv;
    output.alpha = colour.a;
    if (material.tree.x > 0.5) {
        let instance = world_instances[source_index];
        let settings = TreeSettings(material.tree, material.tree_shade, material.tree_size, material.tree_normal);
        let axes = mat3x3f(instance.m0.xyz, instance.m1.xyz, instance.m2.xyz);
        let caster = tree_caster(settings, position, tangent, uv, uv1, uv2, axes, tree_shadow_view(shadow_view.matrix));
        place = caster.position;
        output.uv = caster.uv;
    }
    output.position = caster_position(place, source_index);
    return output;
}

@fragment
fn fs_world_shadow_masked(input: MaskedOutput) {
    let alpha = textureSample(diffuse_map, surface_sampler, input.uv).a;
    let tree = material.tree.x > 0.5;
    if (select(!cut_out_casts(alpha), !tree_casts(alpha), tree)) {
        discard;
    }
}
