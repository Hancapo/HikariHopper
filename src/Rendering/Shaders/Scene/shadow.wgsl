// The key light's shadow map: drawables' depth seen from the sun, through an orthographic view fitted to them.
// Standalone (no Scene/scene.wgsl): its inputs are the view below and the vertices' positions; cut-out (masked) surfaces
// also read their diffuse map, and cast only where the game's shadow pass keeps them. A
// tree's cut-outs are moved first as the game's tree shaders move them into the cascades (Gta5/trees.wgsl, put before this).

struct ShadowView {
    // From origin-relative positions to the shadow map's clip space.
    matrix: mat4x4f,
};

@group(0) @binding(0) var<uniform> shadow_view: ShadowView;

@vertex
fn vs_shadow(@location(0) position: vec3f) -> @builtin(position) vec4f {
    return shadow_view.matrix * vec4f(position, 1.0);
}

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

struct MaskedOutput {
    @builtin(position) position: vec4f,
    @location(0) uv: vec2f,
};

@vertex
fn vs_shadow_masked(@location(0) position: vec3f, @location(2) tangent: vec4f, @location(3) uv: vec2f, @location(4) uv1: vec2f,
    @location(7) uv2: vec2f) -> MaskedOutput {
    var output: MaskedOutput;
    var place = position;
    output.uv = uv;
    if (material.tree.x > 0.5) {
        let settings = TreeSettings(material.tree, material.tree_shade, material.tree_size, material.tree_normal);
        let unplaced = mat3x3f(vec3f(1.0, 0.0, 0.0), vec3f(0.0, 1.0, 0.0), vec3f(0.0, 0.0, 1.0));
        let caster = tree_caster(settings, position, tangent, uv, uv1, uv2, unplaced, tree_shadow_view(shadow_view.matrix));
        place = caster.position;
        output.uv = caster.uv;
    }
    output.position = shadow_view.matrix * vec4f(place, 1.0);
    return output;
}

// A cut-out casts by the cascades' test for cut-outs, hair as the rest; a tree by their own test for trees.
@fragment
fn fs_shadow_masked(input: MaskedOutput) {
    let alpha = textureSample(diffuse_map, surface_sampler, input.uv).a;
    let tree = material.tree.x > 0.5;
    if (select(!cut_out_casts(alpha), !tree_casts(alpha), tree)) {
        discard;
    }
}
